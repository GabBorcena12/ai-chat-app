using AIChatApp.MLTraining.Models;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace AIChatApp.MLTraining.Services;

public interface IResponseReviewer
{
    ResponseReviewResult Review(string question, string answer, string? contextMode = null);
}

public sealed class ResponseReviewerService : IResponseReviewer
{
    private readonly ResponseReviewerOptions _options;
    private readonly Lazy<PredictionEngine<ResponseReviewerInput, ResponseReviewerPrediction>?> _engine;

    public ResponseReviewerService(IOptions<ResponseReviewerOptions> options)
    {
        _options = options.Value;
        _engine = new Lazy<PredictionEngine<ResponseReviewerInput, ResponseReviewerPrediction>?>(CreatePredictionEngine);
    }

    public ResponseReviewResult Review(string question, string answer, string? contextMode = null)
    {
        if (!_options.Enabled)
        {
            return new ResponseReviewResult { IssueType = "Good", Confidence = 1, Source = "Disabled" };
        }

        // Always run deterministic rules first so obvious bad answers are caught even
        // before a trained ML.NET model has been published.
        var heuristic = ReviewWithRules(question, answer, contextMode);
        var engine = _engine.Value;
        if (engine is null)
        {
            return heuristic;
        }

        var prediction = engine.Predict(new ResponseReviewerInput
        {
            Text = BuildFeatureText(question, answer, contextMode)
        });

        var confidence = prediction.Score is { Length: > 0 } ? prediction.Score.Max() : 0f;
        if (string.IsNullOrWhiteSpace(prediction.PredictedLabel))
        {
            return heuristic;
        }

        // Rules still guard obvious leaks/incomplete answers even when an ML.NET model exists.
        if (heuristic.IsRisky && heuristic.Confidence >= confidence)
        {
            return heuristic;
        }

        return new ResponseReviewResult
        {
            IssueType = prediction.PredictedLabel,
            Intent = InferIntent(question, contextMode),
            Confidence = confidence,
            Source = "ML.NET"
        };
    }

    public static string BuildFeatureText(string question, string answer, string? contextMode = null)
        => $"context: {contextMode ?? "unknown"} question: {question} answer: {answer}";

    public static ResponseReviewResult ReviewWithRules(string question, string answer, string? contextMode = null)
    {
        var issue = "Good";
        var confidence = 0.82f;

        if (string.IsNullOrWhiteSpace(answer) || answer.Trim().Length < 12)
        {
            issue = "Incomplete";
            confidence = 0.95f;
        }
        else if (ContainsPromptLeak(answer))
        {
            issue = "PromptLeak";
            confidence = 0.96f;
        }
        else if (LooksRepetitive(answer))
        {
            issue = "Repetitive";
            confidence = 0.9f;
        }
        else if (LooksIncomplete(answer))
        {
            issue = "Incomplete";
            confidence = 0.86f;
        }
        else if (ContradictsMlTrainingWorkflow(question, answer))
        {
            issue = "Incorrect";
            confidence = 0.91f;
        }
        else if (answer.Length > 900)
        {
            issue = "TooLong";
            confidence = 0.72f;
        }

        return new ResponseReviewResult
        {
            IssueType = issue,
            Intent = InferIntent(question, contextMode),
            Confidence = confidence,
            Source = "Rules"
        };
    }

    private PredictionEngine<ResponseReviewerInput, ResponseReviewerPrediction>? CreatePredictionEngine()
    {
        var path = ResolvePath(_options.PublishedModelPath);
        if (!File.Exists(path))
        {
            return null;
        }

        // The prediction engine is lazy-loaded once because ML.NET model loading is
        // relatively expensive compared with a single chat response review.
        var ml = new MLContext(seed: 7);
        var model = ml.Model.Load(path, out _);
        return ml.Model.CreatePredictionEngine<ResponseReviewerInput, ResponseReviewerPrediction>(model);
    }

    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var current = Directory.GetCurrentDirectory();
        var candidate = Path.GetFullPath(Path.Combine(current, path));
        if (File.Exists(candidate))
        {
            return candidate;
        }

        return Path.GetFullPath(Path.Combine(current, "..", path));
    }

    private static bool ContainsPromptLeak(string answer)
        => Regex.IsMatch(answer, @"\b(User question|Assistant answer|Rules|Partial assistant answer|Missing final part only|System:)\b", RegexOptions.IgnoreCase);

    private static bool LooksIncomplete(string answer)
    {
        var trimmed = answer.Trim();
        return !Regex.IsMatch(trimmed, @"[.!?]$")
               || Regex.IsMatch(trimmed, @"\b(and|or|with|for|to|of|in)\s*$", RegexOptions.IgnoreCase)
               || trimmed.EndsWith(':')
               || trimmed.EndsWith(',');
    }

    private static bool LooksRepetitive(string answer)
    {
        var sentences = Regex.Split(answer.Trim(), @"(?<=[.!?])\s+")
            .Select(sentence => Regex.Replace(sentence.ToLowerInvariant(), @"[^\w\s]", " ").Trim())
            .Where(sentence => sentence.Length > 20)
            .ToList();

        if (sentences.Count != sentences.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            return true;
        }

        return sentences.Count >= 3 && sentences.GroupBy(sentence => string.Join(' ', sentence.Split(' ').Take(6)))
            .Any(group => group.Count() > 1);
    }

    private static bool ContradictsMlTrainingWorkflow(string question, string answer)
    {
        var normalizedQuestion = question.ToLowerInvariant();
        if (!normalizedQuestion.Contains("ml training")
            || (!normalizedQuestion.Contains("improve") && !normalizedQuestion.Contains("future chat")))
        {
            return false;
        }

        var normalizedAnswer = answer.ToLowerInvariant();
        return normalizedAnswer.Contains("doesn't improve future chat answers")
               || normalizedAnswer.Contains("does not improve future chat answers")
               || normalizedAnswer.Contains("ml training isn't used")
               || normalizedAnswer.Contains("ml training is not used")
               || normalizedAnswer.Contains("training isn't used")
               || normalizedAnswer.Contains("training is not used");
    }

    private static string InferIntent(string question, string? contextMode)
    {
        var normalized = question.ToLowerInvariant();
        if (normalized.Contains("gateway") || normalized.Contains("header") || normalized.Contains("api key"))
        {
            return "GatewayQuestion";
        }

        if (normalized.Contains("2fa") || normalized.Contains("auth") || normalized.Contains("login") || normalized.Contains("token"))
        {
            return "AuthQuestion";
        }

        if (normalized.Contains("model") || normalized.Contains("llm") || normalized.Contains("qwen") || normalized.Contains("gguf"))
        {
            return "ModelQuestion";
        }

        if (normalized.Contains("ml") || normalized.Contains("training") || normalized.Contains("reviewer"))
        {
            return "MLTrainingQuestion";
        }

        return string.Equals(contextMode, "documentation", StringComparison.OrdinalIgnoreCase)
            ? "DocumentationQuestion"
            : "GeneralQuestion";
    }
}

public sealed class ResponseReviewerInput
{
    public string Text { get; set; } = string.Empty;
    public string Label { get; set; } = "Good";
}

public sealed class ResponseReviewerPrediction
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = string.Empty;

    public float[] Score { get; set; } = [];
}
