using AIChatApp.API.Model;
using AIChatApp.API.Services.Content;
using AIChatApp.API.Services.Generic;
using AIChatApp.API.Services.LLM;
using AIChatApp.API.Services.Processing;
using AIChatApp.API.Services.Prompting;
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

namespace AIChatApp.API.Services.Orchestration
{
    public class ChatOrchestrator
    {
        private const int FastResponseMaxTokens = 96;
        private const int DocumentationResponseMaxTokens = 72;
        private const int ContinueResponseMaxTokens = 160;
        private const int ContinueRetryMaxTokens = 128;
        private const int RetryMaxTokens = 128;
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
            var fastAnswer = await TryGetFastDocumentationAnswerAsync(request);
            if (fastAnswer is not null)
            {
                await _chatHistoryService.SaveMessage(request.ChatId, _assistantName, fastAnswer);
                _logger.LogInformation("Fast documentation answer served for chat {ChatId}.", request.ChatId);
                return fastAnswer;
            }

            var totalStopwatch = Stopwatch.StartNew();
            var llmResponse = await GenerateResponseAsync(request, token);
            totalStopwatch.Stop();
            _logger.LogInformation("AskAsync completed for chat {ChatId} in {ElapsedMs} ms.", request.ChatId, totalStopwatch.ElapsedMilliseconds);
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
                await _chatHistoryService.AppendToLatestAssistantMessageAsync(request.ChatId, continuation, _assistantName);
            }

            return continuation;
        }

        public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            var fastAnswer = await TryGetFastDocumentationAnswerAsync(request);
            if (fastAnswer is not null)
            {
                await _chatHistoryService.SaveMessage(request.ChatId, _assistantName, fastAnswer);
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
            _logger.LogInformation("Prompt build for chat {ChatId} took {ElapsedMs} ms.", request.ChatId, promptStopwatch.ElapsedMilliseconds);

            var buffer = new StringBuilder();
            var maxTokens = GetMaxTokens(request);
            var generationStopwatch = Stopwatch.StartNew();
            var firstTokenLogged = false;
            _logger.LogInformation("Model generation started for chat {ChatId}.", request.ChatId);

            await foreach (var tokenStr in _llm.GenerateAsync(request.User, prompt, maxTokens, token))
            {
                if (!firstTokenLogged)
                {
                    firstTokenLogged = true;
                    _logger.LogInformation(
                        "First token for chat {ChatId}: model_start={ModelStartMs} ms, end_to_end={EndToEndMs} ms.",
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
            _logger.LogInformation("StreamAsync completed for chat {ChatId} in {ElapsedMs} ms.", request.ChatId, totalStopwatch.ElapsedMilliseconds);
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
            _logger.LogInformation("Prompt build for chat {ChatId} took {ElapsedMs} ms.", request.ChatId, promptStopwatch.ElapsedMilliseconds);
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
            _logger.LogInformation("Model generation started for {User}.", user);

            await foreach (var tokenStr in _llm.GenerateAsync(user, prompt, maxTokens, token))
            {
                if (!firstTokenLogged)
                {
                    firstTokenLogged = true;
                    _logger.LogInformation("First token latency for {User}: {ElapsedMs} ms.", user, generationStopwatch.ElapsedMilliseconds);
                }

                buffer.Append(tokenStr);

                if (ShouldStopGeneration(buffer, user, contextMode))
                {
                    break;
                }
            }

            generationStopwatch.Stop();
            _logger.LogInformation("Model generation for {User} finished in {ElapsedMs} ms.", user, generationStopwatch.ElapsedMilliseconds);

            return buffer.ToString();
        }

        private async Task<string> FinalizeResponseAsync(ChatRequest request, string llmResponse, CancellationToken token)
        {
            var finalResponse = string.Empty;
            _logger.LogInformation("First Response : {response}", llmResponse);

            if (!string.IsNullOrWhiteSpace(llmResponse))
            {
                finalResponse = _processor.Clean(llmResponse, request.User);

                // For the non-streaming flow, retry once with a fix prompt to complete it.
                if (ShouldRetryResponse(request, finalResponse, token))
                {
                    finalResponse = await RetryAndFixResponse(finalResponse, request, token);
                }

                var review = _responseReviewer.Review(request.Prompt, finalResponse, request.ContextMode);
                _logger.LogInformation(
                    "Response reviewer result for chat {ChatId}: {IssueType} ({Confidence:P0}) via {Source}.",
                    request.ChatId,
                    review.IssueType,
                    review.Confidence,
                    review.Source);

                if (ShouldRepairReviewedResponse(review, request, token))
                {
                    var repairedResponse = await RetryAndFixResponse(finalResponse, request, token);
                    var repairedReview = _responseReviewer.Review(request.Prompt, repairedResponse, request.ContextMode);
                    _logger.LogInformation(
                        "Response reviewer after repair for chat {ChatId}: {IssueType} ({Confidence:P0}) via {Source}.",
                        request.ChatId,
                        repairedReview.IssueType,
                        repairedReview.Confidence,
                        repairedReview.Source);

                    if (!string.IsNullOrWhiteSpace(repairedResponse)
                        && (!repairedReview.IsRisky || repairedResponse.Length < finalResponse.Length))
                    {
                        finalResponse = repairedResponse;
                    }
                }

                await _chatHistoryService.SaveMessage(request.ChatId, _assistantName, finalResponse);
            }
            else
            {
                finalResponse = _messageUnableToGenerateResponse;
            }

            return finalResponse;
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

        private async Task<string> RetryAndFixResponse(string incompleteResponse, ChatRequest request, CancellationToken cancellationToken)
        {
            // Rebuild Prompt with original + incomplete response + instructions to fix/complete
            var retryPrompt = await _promptBuilder.RebuildPromptWithIncompleteResponseAsync(request.ChatId, request.User, request.Prompt, incompleteResponse, request.ContextMode);
            if (retryPrompt.Length > 5000)
            {
                _logger.LogWarning("Retry prompt too large, using compact repair prompt.");
                retryPrompt = BuildCompactRepairPrompt(request, incompleteResponse);
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
                _logger.LogWarning($"Model is slow (RetryAndFixResponse): {sw.ElapsedMilliseconds} ms");
            }
            else
            {
                _logger.LogInformation($"LLM Success | Latency (RetryAndFixResponse): {sw.ElapsedMilliseconds} ms");
            }

            // Clean and return Response
            var llmResponse = retryBuffer.ToString();
            _logger.LogInformation("Second Response : {1}", llmResponse);
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                _logger.LogWarning("Retry returned empty. Using original response.");
                return incompleteResponse;
            }

            var cleaned = _processor.Clean(llmResponse, request.User);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                _logger.LogWarning("Cleaned retry is empty. Using original response.");
                return incompleteResponse;
            }
            return cleaned;
        }

        private static string BuildCompactRepairPrompt(ChatRequest request, string badResponse)
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
- Keep it concise, practical, and complete.
- If the question is about AIChatApp ML Training, explain that it trains a response-quality reviewer, not the original answer generator.
""";
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
                return answer;
            }

            var matchedAnswer = await TryGetMatchedFastDocumentationAnswerAsync(_assistantProfile.ProfileId, normalized);
            if (!string.IsNullOrWhiteSpace(matchedAnswer))
            {
                return matchedAnswer;
            }

            var topicSummary = await TryGetTopicSummaryAnswerAsync(_assistantProfile.ProfileId, normalized);
            if (!string.IsNullOrWhiteSpace(topicSummary))
            {
                return topicSummary;
            }

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

        private async Task<string?> TryGetMatchedFastDocumentationAnswerAsync(string profileId, string normalizedPrompt)
        {
            try
            {
                var bestMatch = (Answer: (string?)null, Score: 0, Method: string.Empty);
                var entries = await _assistantContentService.LoadQuickAnswersAsync(profileId);
                foreach (var entry in entries)
                {
                    var match = ScoreQuickAnswerEntry(entry, normalizedPrompt);
                    if (match.Score > bestMatch.Score)
                    {
                        bestMatch = (entry.Answer, match.Score, match.Method);
                    }
                }

                if (bestMatch.Score >= 78)
                {
                    _logger.LogInformation("Fast documentation answer matched by {Method} ({Score}%).", bestMatch.Method, bestMatch.Score);
                    return bestMatch.Answer;
                }

                _logger.LogInformation(
                    "No fast documentation answer match across {EntryCount} quick answer(s). Best score: {Score}%.",
                    entries.Count,
                    bestMatch.Score);
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static (int Score, string Method) ScoreQuickAnswerEntry(JsonQuickAnswerEntry entry, string normalizedPrompt)
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

            var fuzzyScore = normalizedAliases
                .Select(alias => ScoreQuickAnswerAlias(alias, normalizedPrompt))
                .DefaultIfEmpty(0)
                .Max();
            if (fuzzyScore >= 85)
            {
                return (fuzzyScore, "close wording");
            }

            var tagScore = ScoreQuickAnswerTags(entry, normalizedPrompt);
            if (tagScore >= 82)
            {
                return (tagScore, "tags");
            }

            var semanticScore = ScoreQuickAnswerSemantic(entry, normalizedPrompt);
            return semanticScore >= 78 ? (semanticScore, "similar meaning") : (0, string.Empty);
        }

        private static int ScoreQuickAnswerAlias(string normalizedAlias, string normalizedPrompt)
        {
            if (string.IsNullOrWhiteSpace(normalizedAlias) || string.IsNullOrWhiteSpace(normalizedPrompt))
            {
                return 0;
            }

            if (string.Equals(normalizedAlias, normalizedPrompt, StringComparison.OrdinalIgnoreCase))
            {
                return 100;
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
    }
}



