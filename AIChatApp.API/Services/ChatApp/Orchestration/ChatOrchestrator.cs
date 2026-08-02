using AIChatApp.API.Models.ChatApp;
using AIChatApp.API.Services.ChatApp.Content;
using AIChatApp.API.Services.ChatApp.History;
using AIChatApp.API.Services.ChatApp.LLM;
using AIChatApp.API.Services.ChatApp.Processing;
using AIChatApp.API.Services.ChatApp.Prompting;
using AIChatApp.Core.Config;
using AIChatApp.Core.Data_Context;
using AIChatApp.MLTraining.Models;
using AIChatApp.MLTraining.Services;
using Azure.Core;
using LLama;
using LLama.Common;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AIChatApp.API.Services.ChatApp.Orchestration
{
    /// <summary>
    /// Coordinates persistence, quick-answer matching, prompt generation, local inference, cleanup, review, repair, and final storage.
    /// Keep transport concerns in controllers and isolate new model or content providers behind existing service interfaces.
    /// </summary>
    public class ChatOrchestrator
    {
        private const int FastResponseMaxTokens = 96;
        private const int DocumentationResponseMaxTokens = 72;
        private const int ContinueResponseMaxTokens = 160;
        private const int ContinueRetryMaxTokens = 128;
        private const int RetryMaxTokens = 128;
        private const string AnsiRed = "\x1b[31m";
        private const string AnsiGreen = "\x1b[32m";
        private const string AnsiCyan = "\x1b[36m";
        private const string AnsiYellow = "\x1b[33m";
        private const string AnsiBlue = "\x1b[34m";
        private const string AnsiMagenta = "\x1b[35m";
        private const string AnsiReset = "\x1b[0m";
        private readonly AppDbContext _dbContext;
        private readonly ILogger<ChatOrchestrator> _logger;
        private readonly ILLMService _llm;
        private readonly IPromptBuilder _promptBuilder;
        private readonly ChatHistoryService _chatHistoryService;
        private readonly IResponseProcessor _processor;
        private readonly Func<InteractiveExecutor> _retryExecutorFactory;
        private string _messageUnableToGenerateResponse;
        private string _assistantName;
        private readonly IConfiguration _configuration;
        private readonly AssistantProfileOptions _assistantProfile;
        private List<string> _antiPrompts = new List<string>();
        private readonly IAssistantContentService _assistantContentService;
        private readonly IResponseReviewer _responseReviewer;

        public ChatOrchestrator(
            ILogger<ChatOrchestrator> logger,
            AppDbContext dbContext,
            ILLMService llm,
            IPromptBuilder promptBuilder,
            IResponseProcessor processor,
            ChatHistoryService chatHistoryService,
            Func<InteractiveExecutor> retryExecutorFactory, 
            IConfiguration configuration,
            IAssistantContentService assistantContentService,
            IResponseReviewer responseReviewer,
            IOptions<AssistantProfileOptions> assistantProfileOptions)
        {
            _retryExecutorFactory = retryExecutorFactory;
            _dbContext = dbContext;
            _logger = logger;
            _llm = llm;
            _chatHistoryService = chatHistoryService;
            _promptBuilder = promptBuilder;
            _processor = processor;
            _assistantProfile = assistantProfileOptions.Value;
            _assistantContentService = assistantContentService;
            _responseReviewer = responseReviewer;
            _assistantName = _assistantProfile.AssistantName;
            _messageUnableToGenerateResponse = "Sorry unable to generate response. Please try again.";
            _configuration = configuration;
            _antiPrompts = _configuration.GetSection("ApiSettings:Services.AntiPrompts").Get<List<string>>() ?? _antiPrompts;
        }

        public async Task<string> AskAsync(ChatRequest request, CancellationToken token)
        {
            await SaveIncomingUserMessageAsync(request);
            var fastAnswer = await TryGetFastDocumentationAnswerAsync(request);
            if (fastAnswer is not null)
            {
                await SaveAssistantMessageAsync(request, fastAnswer);
                _logger.LogInformation("{LogLabel} Saved quick answer served for chat {ChatId}.", LogLabel("[CHAT:QUICK-ANSWER]", AnsiYellow), request.ChatId);
                return fastAnswer;
            }

            var totalStopwatch = Stopwatch.StartNew();
            var llmResponse = await GenerateResponseAsync(request, token);
            totalStopwatch.Stop();
            _logger.LogInformation("{LogLabel} AskAsync completed for chat {ChatId} in {ElapsedMs} ms.", LogLabel("[CHAT:COMPLETE]", AnsiBlue), request.ChatId, totalStopwatch.ElapsedMilliseconds);
            return await FinalizeResponseAsync(request, llmResponse, token);
        }

        public async Task<string> ContinueAsync(ContinueChatRequest request, CancellationToken token)
        {
            var prompt = await _promptBuilder.BuildContinuationPromptAsync(
                request.ChatId,
                request.User,
                request.OriginalPrompt,
                request.PartialResponse,
                request.ContextMode);

            var llmResponse = await GenerateFromPromptAsync(prompt, request.User, request.ContextMode, ContinueResponseMaxTokens, token);
            var continuation = NormalizeContinuationResponse(request, llmResponse);

            if (ShouldRetryContinuation(continuation, request.PartialResponse, token))
            {
                var retryResponse = await GenerateFromPromptAsync(prompt, request.User, request.ContextMode, ContinueRetryMaxTokens, token);
                var retriedContinuation = NormalizeContinuationResponse(request, retryResponse);
                if (!string.IsNullOrWhiteSpace(retriedContinuation))
                {
                    continuation = retriedContinuation;
                }
            }

            if (!string.IsNullOrWhiteSpace(continuation))
            {
                await _chatHistoryService.AppendToLatestAssistantMessageAsync(request.ChatId, continuation, _assistantName, request.UserId);
            }

            return continuation;
        }

        public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            await SaveIncomingUserMessageAsync(request);
            var fastAnswer = await TryGetFastDocumentationAnswerAsync(request);
            if (fastAnswer is not null)
            {
                await SaveAssistantMessageAsync(request, fastAnswer);
                _logger.LogInformation("{LogLabel} Saved quick answer streamed for chat {ChatId}.", LogLabel("[CHAT:QUICK-ANSWER]", AnsiYellow), request.ChatId);
                yield return new ChatStreamChunk
                {
                    Type = "complete",
                    Content = fastAnswer
                };
                yield break;
            }

            var totalStopwatch = Stopwatch.StartNew();
            var promptStopwatch = Stopwatch.StartNew();
            var prompt = await _promptBuilder.BuildPromptAsync(request.ChatId, request.User, request.Prompt, request.ContextMode);
            promptStopwatch.Stop();
            _logger.LogInformation("{LogLabel} Prompt built for chat {ChatId} in {ElapsedMs} ms.", LogLabel("[PROMPT:BUILT]", AnsiMagenta), request.ChatId, promptStopwatch.ElapsedMilliseconds);

            var buffer = new StringBuilder();
            var maxTokens = GetMaxTokens(request);
            var generationStopwatch = Stopwatch.StartNew();
            var firstTokenLogged = false;
            _logger.LogInformation("{LogLabel} Model generation started for chat {ChatId}.", LogLabel("[GENERATE:START]", AnsiCyan), request.ChatId);

            await foreach (var tokenStr in _llm.GenerateAsync(request.User, prompt, maxTokens, token))
            {
                if (!firstTokenLogged)
                {
                    firstTokenLogged = true;
                    _logger.LogInformation(
                        "{LogLabel} First token for chat {ChatId}: model_start={ModelStartMs} ms, end_to_end={EndToEndMs} ms.",
                        LogLabel("[GENERATE:FIRST-TOKEN]", AnsiCyan),
                        request.ChatId,
                        generationStopwatch.ElapsedMilliseconds,
                        totalStopwatch.ElapsedMilliseconds);
                }

                buffer.Append(tokenStr);

                if (ShouldStopGeneration(buffer, request.User, request.ContextMode))
                {
                    break;
                }

                var sanitizedChunk = SanitizeChunk(tokenStr, request.User);
                if (!string.IsNullOrEmpty(sanitizedChunk))
                {
                    yield return new ChatStreamChunk
                    {
                        Type = "token",
                        Content = sanitizedChunk
                    };
                }
            }

            var finalResponse = await FinalizeResponseAsync(request, buffer.ToString(), token);
            totalStopwatch.Stop();
            _logger.LogInformation("{LogLabel} StreamAsync completed for chat {ChatId} in {ElapsedMs} ms.", LogLabel("[CHAT:COMPLETE]", AnsiBlue), request.ChatId, totalStopwatch.ElapsedMilliseconds);
            yield return new ChatStreamChunk
            {
                Type = "complete",
                Content = finalResponse
            };
        }

        private async Task<string> GenerateResponseAsync(ChatRequest request, CancellationToken token)
        {
            var promptStopwatch = Stopwatch.StartNew();
            var prompt = await _promptBuilder.BuildPromptAsync(request.ChatId, request.User, request.Prompt, request.ContextMode);
            promptStopwatch.Stop();
            _logger.LogInformation("{LogLabel} Prompt built for chat {ChatId} in {ElapsedMs} ms.", LogLabel("[PROMPT:BUILT]", AnsiMagenta), request.ChatId, promptStopwatch.ElapsedMilliseconds);
            return await GenerateFromPromptAsync(prompt, request.User, request.ContextMode, GetMaxTokens(request), token);
        }

        private async Task<string> GenerateFromPromptAsync(
            string prompt,
            string user,
            string? contextMode,
            int maxTokens,
            CancellationToken token)
        {
            var buffer = new StringBuilder();
            var generationStopwatch = Stopwatch.StartNew();
            var firstTokenLogged = false;
            _logger.LogInformation("{LogLabel} Model generation started for {User}.", LogLabel("[GENERATE:START]", AnsiCyan), user);

            await foreach (var tokenStr in _llm.GenerateAsync(user, prompt, maxTokens, token))
            {
                if (!firstTokenLogged)
                {
                    firstTokenLogged = true;
                    _logger.LogInformation("{LogLabel} First token latency for {User}: {ElapsedMs} ms.", LogLabel("[GENERATE:FIRST-TOKEN]", AnsiCyan), user, generationStopwatch.ElapsedMilliseconds);
                }

                buffer.Append(tokenStr);

                if (ShouldStopGeneration(buffer, user, contextMode))
                {
                    break;
                }
            }

            generationStopwatch.Stop();
            _logger.LogInformation("{LogLabel} Model generation finished for {User} in {ElapsedMs} ms.", LogLabel("[GENERATE:DONE]", AnsiGreen), user, generationStopwatch.ElapsedMilliseconds);

            return buffer.ToString();
        }

        private async Task<string> FinalizeResponseAsync(ChatRequest request, string llmResponse, CancellationToken token)
        {
            var finalResponse = string.Empty;
            _logger.LogInformation("{LogLabel} Raw model response: {Response}", LogLabel("[RESPONSE:RAW]", AnsiBlue), llmResponse);

            if (!string.IsNullOrWhiteSpace(llmResponse))
            {
                finalResponse = _processor.Clean(llmResponse, request.User);
                finalResponse = EnforceDocumentationAnswerShape(request, finalResponse);

                // For the non-streaming flow, retry once with a fix prompt to complete it.
                if (ShouldRetryResponse(request, finalResponse, token))
                {
                    _logger.LogWarning("{LogLabel} Response looked incomplete. Starting retry/repair for chat {ChatId}.", LogLabel("[RETRY:START]", AnsiYellow), request.ChatId);
                    finalResponse = await RetryAndFixResponse(finalResponse, request, token, completionOnly: true);
                    finalResponse = EnforceDocumentationAnswerShape(request, finalResponse);
                }

                var review = _responseReviewer.Review(request.Prompt, finalResponse, request.ContextMode);
                _logger.LogInformation(
                    "{LogLabel} Response reviewer result for chat {ChatId}: {IssueType} ({Confidence:P0}) via {Source}.",
                    LogLabel(review.IsRisky ? "[REVIEWER:RISK]" : "[REVIEWER:OK]", review.IsRisky ? AnsiYellow : AnsiGreen),
                    request.ChatId,
                    review.IssueType,
                    review.Confidence,
                    review.Source);

                if (ShouldRepairReviewedResponse(review, request, token))
                {
                    _logger.LogWarning("{LogLabel} Reviewer marked response risky. Starting repair for chat {ChatId}.", LogLabel("[REPAIR:START]", AnsiYellow), request.ChatId);
                    var repairedResponse = await RetryAndFixResponse(finalResponse, request, token, completionOnly: false);
                    var repairedReview = _responseReviewer.Review(request.Prompt, repairedResponse, request.ContextMode);
                    _logger.LogInformation(
                        "{LogLabel} Response reviewer after repair for chat {ChatId}: {IssueType} ({Confidence:P0}) via {Source}.",
                        LogLabel(repairedReview.IsRisky ? "[REVIEWER:REPAIR-RISK]" : "[REVIEWER:REPAIR-OK]", repairedReview.IsRisky ? AnsiRed : AnsiGreen),
                        request.ChatId,
                        repairedReview.IssueType,
                        repairedReview.Confidence,
                        repairedReview.Source);

                    if (!string.IsNullOrWhiteSpace(repairedResponse)
                        && (!repairedReview.IsRisky || repairedResponse.Length < finalResponse.Length))
                    {
                        finalResponse = EnforceDocumentationAnswerShape(request, repairedResponse);
                    }
                }

                await SaveAssistantMessageAsync(request, finalResponse);
                _logger.LogInformation("{LogLabel} Final response saved for chat {ChatId}.", LogLabel("[RESPONSE:SAVED]", AnsiGreen), request.ChatId);
            }
            else
            {
                finalResponse = _messageUnableToGenerateResponse;
            }

            return finalResponse;
        }

        private Task SaveIncomingUserMessageAsync(ChatRequest request)
            => _chatHistoryService.SaveMessage(
                request.ChatId,
                request.User,
                request.Prompt,
                request.UserId,
                request.User,
                request.UserMessageId,
                request.ConversationTitle);

        private Task SaveAssistantMessageAsync(ChatRequest request, string response)
            => _chatHistoryService.SaveMessage(
                request.ChatId,
                _assistantName,
                response,
                request.UserId,
                request.User,
                request.AssistantMessageId,
                request.ConversationTitle);

        private static string EnforceDocumentationAnswerShape(ChatRequest request, string response)
        {
            if (!string.Equals(request.ContextMode, "documentation", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(response))
            {
                return response;
            }

            if (AllowsListAnswer(request.Prompt))
            {
                return LimitListOrTechnicalAnswer(response);
            }

            var sentenceLimit = AllowsTwoSentenceAnswer(request.Prompt) ? 2 : 1;
            return TakeCompleteSentences(response, sentenceLimit);
        }

        private static bool AllowsTwoSentenceAnswer(string prompt)
        {
            var normalized = NormalizePrompt(prompt);
            return normalized.StartsWith("how ", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("why ", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("can ", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("does ", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("do ", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("explain", StringComparison.OrdinalIgnoreCase);
        }

        private static bool AllowsListAnswer(string prompt)
        {
            var normalized = NormalizePrompt(prompt);
            return normalized.Contains("list", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("step", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("files", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("file", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("paths", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("where", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("which ", StringComparison.OrdinalIgnoreCase);
        }

        private static string LimitListOrTechnicalAnswer(string response)
        {
            var lines = response
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (lines.Count > 1 && lines.Any(line => line.StartsWith("-") || line.StartsWith("*") || Regex.IsMatch(line, @"^\d+[\.)]\s+")))
            {
                return string.Join(Environment.NewLine, lines.Take(3));
            }

            return TakeCompleteSentences(response, 2);
        }

        private static string TakeCompleteSentences(string response, int maxSentences)
        {
            var normalized = Regex.Replace(response.Trim(), @"\s+", " ");
            var sentences = Regex.Matches(normalized, @".+?[.!?](?=\s|$)")
                .Select(match => match.Value.Trim())
                .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
                .Take(maxSentences)
                .ToList();

            if (sentences.Count > 0)
            {
                return string.Join(" ", sentences);
            }

            return normalized;
        }

        private string SanitizeChunk(string chunk, string user)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                return string.Empty;
            }

            var cleaned = chunk
                .Replace($"{_assistantName}:", string.Empty)
                .Replace($"{user}:", string.Empty)
                .Replace("User:", string.Empty)
                .Replace("Answer:", string.Empty)
                .Replace("Response:", string.Empty)
                .Replace("Rules:", string.Empty)
                .Replace("Final answer:", string.Empty);

            // Strip leading punctuation-only fragments that can appear at the start of generation.
            cleaned = Regex.Replace(cleaned, @"^[^\w\d]+$", string.Empty);
            return cleaned;
        }

        private async Task<string> RetryAndFixResponse(string incompleteResponse, ChatRequest request, CancellationToken cancellationToken, bool completionOnly)
        {
            // Rebuild Prompt with original + incomplete response + instructions to fix/complete
            var retryPrompt = await _promptBuilder.RebuildPromptWithIncompleteResponseAsync(
                request.ChatId,
                request.User,
                request.Prompt,
                incompleteResponse,
                request.ContextMode,
                completionOnly);
            if (retryPrompt.Length > 5000)
            {
                _logger.LogWarning("{LogLabel} Retry prompt too large. Using compact repair prompt.", LogLabel("[RETRY:COMPACT]", AnsiYellow));
                retryPrompt = BuildCompactRepairPrompt(request, incompleteResponse, completionOnly);
            }

            // Get LLM stream (or single output)
            var retryBuffer = new StringBuilder();
            var antiPrompts = new List<string>(_antiPrompts)
            {
                request.User,
                $"{request.User}:",
                "User:",
                $"{_assistantName}:",
                "Response:",
                "Answer:"
            };
            var inferenceParams = new InferenceParams
            {
                MaxTokens = RetryMaxTokens,
                AntiPrompts = antiPrompts.Distinct().ToList()
            };

            var sw = Stopwatch.StartNew();

            // Fresh executor to avoid any state carryover from previous inference
            // Which could impact retries
            await foreach (var token in _retryExecutorFactory().InferAsync(
                retryPrompt.ToString(),
                inferenceParams,
                cancellationToken))
            {
                retryBuffer.Append(token);
            }

            // Log model latency
            sw.Stop();
            if (sw.ElapsedMilliseconds > 30000)
            {
                _logger.LogWarning("{LogLabel} Retry model is slow: {ElapsedMs} ms.", LogLabel("[RETRY:SLOW]", AnsiYellow), sw.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformation("{LogLabel} Retry generation finished in {ElapsedMs} ms.", LogLabel("[RETRY:DONE]", AnsiGreen), sw.ElapsedMilliseconds);
            }

            // Clean and return Response
            var llmResponse = retryBuffer.ToString();
            _logger.LogInformation("{LogLabel} Retry model response: {Response}", LogLabel("[RETRY:RAW]", AnsiBlue), llmResponse);
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                _logger.LogWarning("{LogLabel} Retry returned empty. Using original response.", LogLabel("[RETRY:EMPTY]", AnsiYellow));
                return _processor.Clean(incompleteResponse, request.User);
            }

            var cleaned = _processor.Clean(llmResponse, request.User);
            if (completionOnly && !string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = MergeContinuation(incompleteResponse, cleaned);
            }

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                _logger.LogWarning("{LogLabel} Cleaned retry is empty. Using original response.", LogLabel("[RETRY:EMPTY-CLEAN]", AnsiYellow));
                return _processor.Clean(incompleteResponse, request.User);
            }

            if (LooksLikeLeakedRepairInstruction(llmResponse) || LooksLikeLeakedRepairInstruction(cleaned))
            {
                _logger.LogWarning("{LogLabel} Retry output looked like leaked repair instructions. Using original response.", LogLabel("[RETRY:REJECTED]", AnsiRed));
                return _processor.Clean(incompleteResponse, request.User);
            }

            if (ShouldRetryResponse(request, cleaned, cancellationToken))
            {
                _logger.LogWarning("{LogLabel} Retry output still looked incomplete. Using original response.", LogLabel("[RETRY:INCOMPLETE]", AnsiYellow));
                return _processor.Clean(incompleteResponse, request.User);
            }

            return cleaned;
        }

        private static string MergeContinuation(string incompleteResponse, string continuation)
        {
            var original = incompleteResponse.Trim();
            var ending = continuation.Trim();
            if (string.IsNullOrWhiteSpace(ending))
            {
                return original;
            }

            if (ending.StartsWith(original, StringComparison.OrdinalIgnoreCase))
            {
                return ending;
            }

            var normalizedOriginal = NormalizePrompt(original);
            var normalizedEnding = NormalizePrompt(ending);
            if (normalizedOriginal.Contains(normalizedEnding, StringComparison.OrdinalIgnoreCase))
            {
                return original;
            }

            var separator = original.EndsWith("-") || original.EndsWith("/") ? string.Empty : " ";
            return $"{original.TrimEnd(',', ':', ';')}{separator}{ending}".Trim();
        }

        private static string BuildCompactRepairPrompt(ChatRequest request, string badResponse, bool completionOnly)
        {
            var safeBadResponse = badResponse.Length > 1400
                ? badResponse[..1400]
                : badResponse;

            var safeQuestion = request.Prompt.Length > 900
                ? request.Prompt[..900]
                : request.Prompt;

            var context = string.Equals(request.ContextMode, "documentation", StringComparison.OrdinalIgnoreCase)
                ? "Use only the AIChatApp project context. Be specific and correct."
                : "Answer the user's question directly and correctly.";

            if (completionOnly)
            {
                return $"""
System: You are completing an unfinished assistant answer.
{context}

User question:
{safeQuestion}

Unfinished assistant answer:
{safeBadResponse}

Return only the missing final words or final sentence.
Rules:
- Do not restart or rewrite the answer.
- Do not add examples, recap phrases, closing offers, or transition words.
- If the answer is already complete, return an empty response.
""";
            }

            return $"""
System: You are repairing a low-quality assistant response.
{context}

User question:
{safeQuestion}

Bad assistant response:
{safeBadResponse}

Write a corrected final answer.
Rules:
- Do not say the previous answer was bad.
- Do not include labels like User, Assistant, Response, or Answer.
- Prefer one complete sentence.
- Use two short sentences only when one sentence would be unclear.
- Do not add recap phrases, closing offers, or transition words like hence, therefore, additionally, or to summarize.
- Do not introduce unrelated topics that were not asked.
""";
        }

        private static bool LooksLikeLeakedRepairInstruction(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            var leakedInstructionMarkers = new[]
            {
                "For other issues, explain",
                "For other topics, explain",
                "Do not include examples",
                "Use bullet points if needed",
                "Do not explain the topic",
                "response generator has been updated",
                "trains a response generator"
            };

            return leakedInstructionMarkers.Any(marker =>
                response.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        private bool ShouldRetryResponse(ChatRequest request, string response, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return false;
            }

            if (string.Equals(request.ContextMode, "documentation", StringComparison.OrdinalIgnoreCase))
            {
                return IsClearlyIncompleteDocumentationResponse(response);
            }

            return _processor.IsIncomplete(response);
        }

        private static bool ShouldRepairReviewedResponse(
            ResponseReviewResult review,
            ChatRequest request,
            CancellationToken token)
        {
            if (token.IsCancellationRequested || !review.IsRisky)
            {
                return false;
            }

            if (!string.Equals(request.ContextMode, "documentation", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return review.IssueType is "Incorrect" or "Incomplete" or "Repetitive" or "PromptLeak" or "TooLong";
        }

        private bool ShouldStopGeneration(StringBuilder buffer, string user, string? contextMode)
        {
            var tailLength = Math.Min(buffer.Length, 220);
            if (tailLength <= 0)
            {
                return false;
            }

            var tail = buffer.ToString(buffer.Length - tailLength, tailLength);
            return tail.Contains("\nUser:", StringComparison.OrdinalIgnoreCase)
                   || tail.Contains($"\n{user}:", StringComparison.OrdinalIgnoreCase)
                   || tail.Contains($"\n{_assistantName}:", StringComparison.OrdinalIgnoreCase)
                   || tail.Contains("\nPrompt:", StringComparison.OrdinalIgnoreCase)
                   || tail.Contains("\nResponse:", StringComparison.OrdinalIgnoreCase)
                   || tail.Contains("\nAnswer:", StringComparison.OrdinalIgnoreCase)
                   || tail.Contains("\nRules:", StringComparison.OrdinalIgnoreCase)
                   || tail.Contains("\nOriginal user question:", StringComparison.OrdinalIgnoreCase)
                   || tail.Contains("\nAnswer so far:", StringComparison.OrdinalIgnoreCase)
                   || tail.Contains("\nIncomplete answer to fix:", StringComparison.OrdinalIgnoreCase)
                   || tail.Contains("\nFinal answer:", StringComparison.OrdinalIgnoreCase)
                   || tail.Contains("\nMissing final part only:", StringComparison.OrdinalIgnoreCase);
        }

        private string NormalizeContinuationResponse(ContinueChatRequest request, string llmResponse)
        {
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                return string.Empty;
            }

            var cleaned = _processor.Clean(llmResponse, request.User);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            if (string.Equals(cleaned.Trim(), request.PartialResponse.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return ExtractMissingContinuation(request.PartialResponse, cleaned);
        }

        private static string ExtractMissingContinuation(string partialResponse, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return string.Empty;
            }

            var trimmedCandidate = candidate.Trim();
            var trimmedPartial = partialResponse?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(trimmedPartial)
                && trimmedCandidate.StartsWith(trimmedPartial, StringComparison.OrdinalIgnoreCase))
            {
                trimmedCandidate = trimmedCandidate[trimmedPartial.Length..].TrimStart();
            }

            var overlap = FindOverlapLength(trimmedPartial, trimmedCandidate);
            if (overlap > 0)
            {
                trimmedCandidate = trimmedCandidate[overlap..].TrimStart();
            }

            return trimmedCandidate.Trim();
        }

        private static int FindOverlapLength(string original, string continuation)
        {
            var max = Math.Min(original.Length, continuation.Length);
            for (var length = max; length >= 12; length--)
            {
                if (original.EndsWith(continuation[..length], StringComparison.OrdinalIgnoreCase))
                {
                    return length;
                }
            }

            return 0;
        }

        private bool ShouldRetryContinuation(string continuation, string partialResponse, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(continuation))
            {
                return true;
            }

            if (continuation.Length < 12 && partialResponse.TrimEnd().EndsWith("...", StringComparison.Ordinal))
            {
                return true;
            }

            return IsClearlyIncompleteDocumentationResponse(continuation);
        }

        private async Task<string?> TryGetFastDocumentationAnswerAsync(ChatRequest request)
        {
            if (!string.Equals(request.ContextMode, "documentation", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return null;
            }

            var normalized = NormalizePrompt(request.Prompt);
            var answers = await BuildFastDocumentationAnswersAsync();
            if (answers.TryGetValue($"{_assistantProfile.ProfileId}|{normalized}", out var answer))
            {
                _logger.LogInformation(
                    "{LogLabel} Exact saved question match for profile {ProfileId}. Chat will return the saved quick answer.",
                    LogLabel("[MATCH:EXACT]", AnsiYellow),
                    _assistantProfile.ProfileId);
                return answer;
            }

            var intent = ClassifyFastDocumentationIntent(normalized);
            var matchedAnswer = await TryGetMatchedFastDocumentationAnswerAsync(_assistantProfile.ProfileId, normalized, intent);
            if (!string.IsNullOrWhiteSpace(matchedAnswer))
            {
                return matchedAnswer;
            }

            _logger.LogInformation(
                "{LogLabel} No safe saved quick answer match for {Intent} intent. Chat will use normal AI generation with retrieved context.",
                LogLabel("[CHAT:LLM]", AnsiCyan),
                intent);
            return null;
        }

        private static bool IsClearlyIncompleteDocumentationResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return true;
            }

            var trimmed = response.Trim();
            if (trimmed.Length < 12)
            {
                return true;
            }

            if (trimmed.EndsWith(":") || trimmed.EndsWith("-") || trimmed.EndsWith(","))
            {
                return true;
            }

            if (Regex.IsMatch(trimmed, @"\b(and|or|with|for|to|of|in)\s*$", RegexOptions.IgnoreCase))
            {
                return true;
            }

            return !Regex.IsMatch(trimmed, @"[.!?]$");
        }

        private static int GetMaxTokens(ChatRequest request)
        {
            return string.Equals(request.ContextMode, "documentation", StringComparison.OrdinalIgnoreCase)
                ? DocumentationResponseMaxTokens
                : FastResponseMaxTokens;
        }

        private static string NormalizePrompt(string prompt)
            => Regex.Replace(prompt.Trim().ToLowerInvariant(), @"[^\w\s]", " ")
                .Replace("  ", " ")
                .Trim();

        private async Task<IReadOnlyDictionary<string, string>> BuildFastDocumentationAnswersAsync()
        {
            var answers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var profileId in new[] { _assistantProfile.ProfileId })
                {
                    foreach (var entry in await _assistantContentService.LoadQuickAnswersAsync(profileId))
                    {
                        foreach (var alias in entry.Aliases)
                        {
                            answers[$"{profileId}|{NormalizePrompt(alias)}"] = entry.Answer;
                        }
                    }
                }
            }
            catch
            {
            }

            return answers;
        }

        private async Task<string?> TryGetMatchedFastDocumentationAnswerAsync(string profileId, string normalizedPrompt, FastDocumentationIntent intent)
        {
            try
            {
                var bestMatch = (Answer: (string?)null, Score: 0, Method: string.Empty, Title: string.Empty);
                var entries = await _assistantContentService.LoadQuickAnswersAsync(profileId);
                foreach (var entry in entries)
                {
                    var match = ScoreQuickAnswerEntry(entry, normalizedPrompt, intent);
                    if (match.Score > bestMatch.Score)
                    {
                        bestMatch = (entry.Answer, match.Score, match.Method, entry.Title);
                    }
                }

                if (bestMatch.Score >= 78)
                {
                    _logger.LogInformation(
                        "{LogLabel} Saved quick answer '{Title}' matched by {Method} ({Score}%) for {Intent} intent.",
                        LogLabel("[MATCH:SAFE]", AnsiGreen),
                        bestMatch.Title,
                        bestMatch.Method,
                        bestMatch.Score,
                        intent);
                    return bestMatch.Answer;
                }

                _logger.LogInformation(
                    "{LogLabel} No direct quick answer across {EntryCount} quick answer(s) for {Intent} intent. Best score: {Score}%.",
                    LogLabel("[MATCH:NONE]", AnsiCyan),
                    entries.Count,
                    intent,
                    bestMatch.Score);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "{LogLabel} Quick-answer matching failed. Chat will continue with normal AI generation.",
                    LogLabel("[MATCH:ERROR]", AnsiYellow));
                return null;
            }
        }

        private static string LogLabel(string label, string color)
        {
            if (Console.IsOutputRedirected || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NO_COLOR")))
            {
                return label;
            }

            return $"{color}{label}{AnsiReset}";
        }

        private static (int Score, string Method) ScoreQuickAnswerEntry(JsonQuickAnswerEntry entry, string normalizedPrompt, FastDocumentationIntent intent)
        {
            var normalizedAliases = entry.Aliases
                .Concat([entry.Title])
                .Select(NormalizePrompt)
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .ToList();

            var exactScore = normalizedAliases.Any(alias => string.Equals(alias, normalizedPrompt, StringComparison.OrdinalIgnoreCase))
                ? 100
                : 0;
            if (exactScore > 0)
            {
                return (exactScore, "exact alias");
            }

            if (intent != FastDocumentationIntent.General && !QuickAnswerMatchesIntent(entry, intent))
            {
                return (0, string.Empty);
            }

            var promptShape = GetQuestionShape(normalizedPrompt);
            var fuzzyScore = normalizedAliases
                .Select(alias => ScoreQuickAnswerAlias(alias, normalizedPrompt, promptShape))
                .DefaultIfEmpty(0)
                .Max();
            if (fuzzyScore >= 92)
            {
                return (fuzzyScore, "strong close wording");
            }

            var tagScore = ScoreQuickAnswerTags(entry, normalizedPrompt);
            if (fuzzyScore >= 80 && tagScore >= 45)
            {
                return (Math.Max(fuzzyScore, tagScore), "close wording with tags/source");
            }

            return (0, string.Empty);
        }

        private static FastDocumentationIntent ClassifyFastDocumentationIntent(string normalizedPrompt)
        {
            if (ContainsAny(normalizedPrompt, "llm", "model", "gguf", "qwen", "llama", "localmodel", "context size"))
            {
                return FastDocumentationIntent.Model;
            }

            if (ContainsAny(normalizedPrompt, "gateway", "port", "ports", "header", "routing", "route"))
            {
                return FastDocumentationIntent.Gateway;
            }

            if (ContainsAny(normalizedPrompt, "login", "signin", "sign in", "register", "auth", "2fa", "token", "jwt", "google authenticator"))
            {
                return FastDocumentationIntent.Auth;
            }

            if (ContainsAny(normalizedPrompt, "docker", "container", "compose", "sql", "connection"))
            {
                return FastDocumentationIntent.Setup;
            }

            if (ContainsAny(normalizedPrompt, "ml", "machine learning", "training", "reviewer"))
            {
                return FastDocumentationIntent.MachineLearning;
            }

            return FastDocumentationIntent.General;
        }

        private static bool QuickAnswerMatchesIntent(JsonQuickAnswerEntry entry, FastDocumentationIntent intent)
        {
            var searchableText = NormalizePrompt(string.Join(' ', entry.Aliases
                .Concat(entry.Keywords)
                .Concat([entry.Title, entry.SourceName, entry.Summary, entry.Answer])));

            return intent switch
            {
                FastDocumentationIntent.Model => ContainsAny(searchableText, "llm", "model", "gguf", "qwen", "llama", "localmodel", "context size"),
                FastDocumentationIntent.Gateway => ContainsAny(searchableText, "gateway", "port", "ports", "header", "routing", "route"),
                FastDocumentationIntent.Auth => ContainsAny(searchableText, "login", "signin", "sign in", "register", "auth", "2fa", "token", "jwt", "google authenticator"),
                FastDocumentationIntent.Setup => ContainsAny(searchableText, "docker", "container", "compose", "sql", "connection"),
                FastDocumentationIntent.MachineLearning => ContainsAny(searchableText, "ml", "machine learning", "training", "reviewer"),
                _ => true
            };
        }

        private static bool ContainsAny(string value, params string[] keywords)
            => keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        private static int ScoreQuickAnswerAlias(string normalizedAlias, string normalizedPrompt, QuestionShape promptShape)
        {
            if (string.IsNullOrWhiteSpace(normalizedAlias) || string.IsNullOrWhiteSpace(normalizedPrompt))
            {
                return 0;
            }

            if (string.Equals(normalizedAlias, normalizedPrompt, StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            var aliasShape = GetQuestionShape(normalizedAlias);
            if (promptShape != QuestionShape.Unknown
                && aliasShape != QuestionShape.Unknown
                && promptShape != aliasShape)
            {
                return 0;
            }

            if (normalizedAlias.Contains(normalizedPrompt, StringComparison.OrdinalIgnoreCase)
                || normalizedPrompt.Contains(normalizedAlias, StringComparison.OrdinalIgnoreCase))
            {
                return 95;
            }

            var aliasTokens = GetMeaningfulMatchTokens(normalizedAlias);
            var promptTokens = GetMeaningfulMatchTokens(normalizedPrompt);
            if (aliasTokens.Count < 3 || promptTokens.Count < 3)
            {
                return 0;
            }

            var overlap = aliasTokens.Count(promptTokens.Contains);
            var promptCoverage = overlap / (double)promptTokens.Count;
            var aliasCoverage = overlap / (double)aliasTokens.Count;

            return promptCoverage >= 0.85 && aliasCoverage >= 0.65
                ? (int)Math.Round((promptCoverage * 60) + (aliasCoverage * 40))
                : 0;
        }

        private static QuestionShape GetQuestionShape(string normalizedText)
        {
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                return QuestionShape.Unknown;
            }

            var first = normalizedText
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? string.Empty;

            return first switch
            {
                "what" => QuestionShape.What,
                "where" => QuestionShape.Where,
                "when" => QuestionShape.When,
                "why" => QuestionShape.Why,
                "how" => QuestionShape.How,
                "which" => QuestionShape.Which,
                "who" => QuestionShape.Who,
                "can" => QuestionShape.Can,
                "does" or "do" => QuestionShape.Does,
                "is" or "are" => QuestionShape.Is,
                _ => QuestionShape.Unknown
            };
        }

        private static int ScoreQuickAnswerTags(JsonQuickAnswerEntry entry, string normalizedPrompt)
        {
            var tagTexts = entry.Keywords
                .Concat([entry.SourceName, entry.Summary])
                .Select(NormalizePrompt)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .ToList();

            if (tagTexts.Count == 0)
            {
                return 0;
            }

            if (tagTexts.Any(tag => normalizedPrompt.Contains(tag, StringComparison.OrdinalIgnoreCase)))
            {
                return 94;
            }

            var promptTokens = GetMeaningfulMatchTokens(normalizedPrompt);
            var tagTokens = tagTexts
                .SelectMany(GetMeaningfulMatchTokens)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (tagTokens.Count == 0 || promptTokens.Count == 0)
            {
                return 0;
            }

            var overlap = tagTokens.Count(promptTokens.Contains);
            var tagCoverage = overlap / (double)tagTokens.Count;
            return tagCoverage >= 0.75 ? (int)Math.Round(tagCoverage * 90) : 0;
        }

        private static int ScoreQuickAnswerSemantic(JsonQuickAnswerEntry entry, string normalizedPrompt)
        {
            var promptConcepts = BuildSemanticConcepts(normalizedPrompt);
            var entryConcepts = entry.Aliases
                .Concat(entry.Keywords)
                .Concat([entry.Title, entry.SourceName, entry.Summary, entry.Answer])
                .SelectMany(value => BuildSemanticConcepts(NormalizePrompt(value)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (promptConcepts.Count < 3 || entryConcepts.Count < 3)
            {
                return 0;
            }

            var overlap = promptConcepts.Count(entryConcepts.Contains);
            var precision = overlap / (double)promptConcepts.Count;
            var recall = overlap / (double)entryConcepts.Count;
            var harmonic = precision + recall == 0 ? 0 : (2 * precision * recall) / (precision + recall);

            return (int)Math.Round(harmonic * 100);
        }

        private static HashSet<string> BuildSemanticConcepts(string value)
        {
            var concepts = GetMeaningfulMatchTokens(value)
                .Select(NormalizeConcept)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var concept in concepts.ToList())
            {
                foreach (var related in ExpandRelatedConcepts(concept))
                {
                    concepts.Add(related);
                }
            }

            if (value.Contains("machine learning", StringComparison.OrdinalIgnoreCase))
            {
                concepts.Add("ml");
            }

            if (value.Contains("ml training", StringComparison.OrdinalIgnoreCase))
            {
                concepts.Add("reviewer");
                concepts.Add("quality");
            }

            if (value.Contains("reviewer model", StringComparison.OrdinalIgnoreCase))
            {
                concepts.Add("ml");
                concepts.Add("training");
                concepts.Add("quality");
            }

            return concepts;
        }

        private static string NormalizeConcept(string token)
        {
            if (token.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && token.Length > 4)
            {
                return $"{token[..^3]}y";
            }

            if (token.EndsWith("ing", StringComparison.OrdinalIgnoreCase) && token.Length > 5)
            {
                return token[..^3];
            }

            if (token.EndsWith("ed", StringComparison.OrdinalIgnoreCase) && token.Length > 4)
            {
                return token[..^2];
            }

            return token.EndsWith('s') && token.Length > 3 ? token[..^1] : token;
        }

        private static IReadOnlyList<string> ExpandRelatedConcepts(string concept)
            => concept switch
            {
                "answer" or "response" or "reply" => ["answer", "response", "reply"],
                "improve" or "improvement" or "better" or "quality" => ["improve", "better", "quality"],
                "future" or "later" => ["future", "later"],
                "train" or "training" => ["train", "training"],
                "review" or "reviewer" or "classify" or "classification" => ["review", "reviewer", "classify", "quality"],
                "ml" or "machine" or "learning" => ["ml", "machine", "learning"],
                _ => []
            };

        private static HashSet<string> GetMeaningfulMatchTokens(string value)
        {
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "a", "an", "and", "are", "can", "do", "does", "for", "how", "in", "is", "it", "of", "on", "or", "the", "to", "what", "when", "where", "why", "with"
            };

            return value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => token.Length > 1 && !stopWords.Contains(token))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private async Task<string?> TryGetTopicSummaryAnswerAsync(string profileId, string normalizedPrompt)
        {
            try
            {
                var topics = (await _assistantContentService.LoadTopicsAsync(profileId))
                    .Select(entry => new TopicSummaryEntry(entry.Topic, entry.Summary, entry.Keywords))
                    .ToList();

                var best = topics
                    .Select(topic => new
                    {
                        Topic = topic,
                        Score = ScoreTopic(topic, normalizedPrompt)
                    })
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();

                return best is { Score: >= 25 } ? best.Topic.Summary : null;
            }
            catch
            {
                return null;
            }
        }

        private static int ScoreTopic(TopicSummaryEntry topic, string normalizedPrompt)
        {
            var score = 0;
            var normalizedTopic = NormalizePrompt(topic.Topic);
            if (!string.IsNullOrWhiteSpace(normalizedTopic)
                && (normalizedPrompt.Contains(normalizedTopic, StringComparison.OrdinalIgnoreCase)
                    || normalizedTopic.Contains(normalizedPrompt, StringComparison.OrdinalIgnoreCase)))
            {
                score += 40;
            }

            foreach (var keyword in topic.Keywords)
            {
                var normalizedKeyword = NormalizePrompt(keyword);
                if (!string.IsNullOrWhiteSpace(normalizedKeyword)
                    && normalizedPrompt.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    score += 15;
                }
            }

            return score;
        }

        private sealed record TopicSummaryEntry(string Topic, string Summary, IReadOnlyList<string> Keywords);

        private enum FastDocumentationIntent
        {
            General,
            Auth,
            Gateway,
            MachineLearning,
            Model,
            Setup
        }

        private enum QuestionShape
        {
            Unknown,
            What,
            Where,
            When,
            Why,
            How,
            Which,
            Who,
            Can,
            Does,
            Is
        }
    }
}



