using AIChatApp.MLTraining.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AIChatApp.MLTraining.Services;

// Owns the ML training workflow state for the Backoffice Machine Learning workspace.
// Triggered by Backoffice UI actions such as importing text,
// approving reviewed examples, building datasets, running training, and publishing models.
// This is currently an in-memory workflow simulation; replace the storage/training runner
// with database + ML.NET services when the training pipeline becomes production-ready.
public sealed class TrainingWorkspaceService
{
    private readonly ResponseReviewerOptions _options;
    private readonly ResponseReviewerTrainer _trainer = new();
    private readonly List<TrainingExample> _examples = [];
    private readonly List<TrainingDataset> _datasets = [];
    private readonly List<TrainingJob> _jobs = [];
    private readonly List<ModelVersion> _models = [];
    private int _nextExampleId = 1;
    private int _nextDatasetId = 1;
    private int _nextJobId = 1;
    private int _nextModelId = 1;

    public TrainingWorkspaceService()
        : this(Options.Create(new ResponseReviewerOptions()))
    {
    }

    public TrainingWorkspaceService(IOptions<ResponseReviewerOptions> options)
    {
        _options = options.Value;
        // Seeds reviewer examples so the first local training run has every quality label.
        // Trigger: service startup / dependency injection creates the singleton instance.
        SeedReviewerTrainingExamples();
    }

    // Read-only views used by Backoffice to display the current ML workflow state:
    // examples are training rows, datasets are frozen snapshots, jobs are train runs,
    // models are generated ZIP versions, and latest values drive Train/Publish actions.
    public IReadOnlyList<TrainingExample> Examples => _examples
        .OrderByDescending(example => example.CreatedAt)
        .ToList();

    public IReadOnlyList<TrainingDataset> Datasets => _datasets
        .OrderByDescending(dataset => dataset.CreatedAt)
        .ToList();

    public IReadOnlyList<TrainingJob> Jobs => _jobs
        .OrderByDescending(job => job.QueuedAt)
        .ToList();

    public IReadOnlyList<ModelVersion> Models => _models
        .OrderByDescending(model => model.CreatedAt)
        .ToList();

    public TrainingDataset? LatestDataset => _datasets
        .OrderByDescending(dataset => dataset.CreatedAt)
        .FirstOrDefault();

    public ModelVersion? LatestModel => _models
        .OrderByDescending(model => model.CreatedAt)
        .FirstOrDefault();

    public ReviewerWorkflowState GetState()
    {
        var published = _models.FirstOrDefault(model => model.IsPublished);
        return new ReviewerWorkflowState
        {
            ApprovedExamples = _examples.Count(example => example.ApprovedForTraining),
            DatasetCount = _datasets.Count,
            JobCount = _jobs.Count,
            ModelCount = _models.Count,
            PublishedModelVersion = published?.Version ?? string.Empty,
            PublishedModelPath = published is null ? string.Empty : ResolvePath(_options.PublishedModelPath)
        };
    }

    public int ImportApprovedExamples(IEnumerable<TrainingExample> examples)
    {
        var imported = 0;
        foreach (var source in examples)
        {
            if (string.IsNullOrWhiteSpace(source.SourceReference))
            {
                continue;
            }

            var existing = _examples.FirstOrDefault(example =>
                string.Equals(example.SourceReference, source.SourceReference, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                source.Id = _nextExampleId++;
                source.SourceType = string.IsNullOrWhiteSpace(source.SourceType) ? "ReviewedReport" : source.SourceType;
                source.ReviewStatus = "Approved";
                source.ApprovedForTraining = true;
                source.ReviewedAt ??= DateTime.UtcNow;
                _examples.Add(source);
                imported++;
                continue;
            }

            existing.Question = source.Question;
            existing.BadResponse = source.BadResponse;
            existing.ExpectedAnswer = source.ExpectedAnswer;
            existing.IssueType = source.IssueType;
            existing.Intent = source.Intent;
            existing.ReviewStatus = "Approved";
            existing.ApprovedForTraining = true;
            existing.ReviewedBy = source.ReviewedBy;
            existing.ReviewedAt = source.ReviewedAt ?? DateTime.UtcNow;
        }

        return imported;
    }

    public async Task<int> ImportTextFileAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        // Imports raw text into draft examples that still need human labeling/review.
        // Trigger: Training Data UI -> text file upload.
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var chunks = content
            .Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(chunk => chunk.Length > 20)
            .Take(100)
            .ToList();

        foreach (var chunk in chunks)
        {
            _examples.Add(new TrainingExample
            {
                Id = _nextExampleId++,
                SourceType = "UploadedText",
                SourceReference = fileName,
                Question = "Needs reviewer question",
                BadResponse = string.Empty,
                ExpectedAnswer = chunk,
                Intent = "NeedsLabel",
                IssueType = "NeedsLabel",
                ReviewStatus = "Draft"
            });
        }

        return chunks.Count;
    }

    public void ApproveExample(int id, string reviewer)
    {
        // Marks a reviewed/draft example as safe to include in the next dataset.
        // Trigger: Training Data UI -> "Approve" or "Approve all".
        var example = _examples.FirstOrDefault(item => item.Id == id);
        if (example is null)
        {
            return;
        }

        example.ReviewStatus = "Approved";
        example.ApprovedForTraining = true;
        example.ReviewedBy = reviewer;
        example.ReviewedAt = DateTime.UtcNow;
    }

    public TrainingDataset BuildDataset(string name)
    {
        // Creates a dataset snapshot from currently approved examples.
        // Trigger: Training Data UI -> "Build dataset".
        var approvedCount = _examples.Count(example => example.ApprovedForTraining);
        var approvedIds = _examples
            .Where(example => example.ApprovedForTraining)
            .Select(example => example.Id)
            .ToList();
        var version = _datasets.Count(dataset => string.Equals(dataset.Name, name, StringComparison.OrdinalIgnoreCase)) + 1;
        var dataset = new TrainingDataset
        {
            Id = _nextDatasetId++,
            Name = string.IsNullOrWhiteSpace(name) ? "DocumentationQuality" : name.Trim(),
            Version = version,
            ExampleCount = approvedCount,
            ExampleIds = approvedIds
        };

        _datasets.Add(dataset);
        return dataset;
    }

    public async Task<TrainingJob> QueueAndRunTrainingAsync(int datasetId, string triggeredBy, CancellationToken cancellationToken)
    {
        // Starts a training job for a dataset and creates a model version when complete.
        // Trigger: Training Jobs UI -> "Queue training".
        // Current behavior simulates training metrics instead of invoking ML.NET.
        var dataset = _datasets.FirstOrDefault(item => item.Id == datasetId)
            ?? throw new InvalidOperationException("Build a dataset before training.");

        if (dataset.ExampleCount == 0)
        {
            throw new InvalidOperationException("The dataset has no approved examples.");
        }

        var job = new TrainingJob
        {
            Id = _nextJobId++,
            DatasetId = dataset.Id,
            TriggeredBy = string.IsNullOrWhiteSpace(triggeredBy) ? "admin" : triggeredBy.Trim()
        };

        _jobs.Add(job);
        job.Status = "Running";
        job.StartedAt = DateTime.UtcNow;

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var approvedExamples = _examples
            .Where(example => dataset.ExampleIds.Contains(example.Id))
            .ToList();
        var candidateModelPath = BuildCandidateModelPath(dataset, job);
        var result = _trainer.TrainAndSave(approvedExamples, candidateModelPath);

        job.Accuracy = result.Accuracy.HasValue ? Math.Round(result.Accuracy.Value, 3) : null;
        job.F1Score = result.F1Score.HasValue ? Math.Round(result.F1Score.Value, 3) : null;
        job.Status = "Completed";
        job.CompletedAt = DateTime.UtcNow;
        job.Notes = $"ML.NET reviewer trained with {result.ExampleCount} approved example(s) and {result.LabelCount} label(s). {result.MetricNote}";

        _models.Add(new ModelVersion
        {
            Id = _nextModelId++,
            TrainingJobId = job.Id,
            ModelName = dataset.Name,
            Version = $"v{dataset.Version}.{job.Id}",
            ModelPath = result.ModelPath,
            IsPublished = false
        });

        return job;
    }

    public void PublishModel(int modelId)
    {
        // Makes one model version active and unpublishes the rest.
        // Trigger: Model Registry UI -> "Publish".
        var selectedModel = _models.FirstOrDefault(model => model.Id == modelId)
            ?? throw new InvalidOperationException("Model version not found.");

        var publishedPath = ResolvePath(_options.PublishedModelPath);
        Directory.CreateDirectory(Path.GetDirectoryName(publishedPath)!);
        File.Copy(selectedModel.ModelPath, publishedPath, overwrite: true);

        foreach (var model in _models)
        {
            model.IsPublished = model.Id == modelId;
        }
    }

    private string BuildCandidateModelPath(TrainingDataset dataset, TrainingJob job)
    {
        var folder = ResolvePath(_options.CandidateModelFolder);
        Directory.CreateDirectory(folder);
        var safeName = string.Join("-", dataset.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return Path.Combine(folder, $"{safeName}-v{dataset.Version}-job{job.Id}.zip");
    }

    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var current = Directory.GetCurrentDirectory();
        var direct = Path.GetFullPath(Path.Combine(current, path));
        var firstSegment = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (File.Exists(direct)
            || Directory.Exists(Path.GetDirectoryName(direct))
            || (!string.IsNullOrWhiteSpace(firstSegment) && Directory.Exists(Path.Combine(current, firstSegment))))
        {
            return direct;
        }

        return Path.GetFullPath(Path.Combine(current, "..", path));
    }

    public int ImportPublishedKnowledgeEntries(IEnumerable<TrainingExample> examples)
    {
        var imported = 0;
        foreach (var source in examples)
        {
            if (string.IsNullOrWhiteSpace(source.SourceReference))
            {
                continue;
            }

            var existing = _examples.FirstOrDefault(example =>
                string.Equals(example.SourceReference, source.SourceReference, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                source.Id = _nextExampleId++;
                source.SourceType = string.IsNullOrWhiteSpace(source.SourceType) ? "PublishedKnowledgeEntry" : source.SourceType;
                source.IssueType = "Good";
                source.ReviewStatus = "Approved";
                source.ApprovedForTraining = true;
                source.ReviewedAt ??= DateTime.UtcNow;
                _examples.Add(source);
                imported++;
                continue;
            }

            existing.Question = source.Question;
            existing.BadResponse = source.BadResponse;
            existing.ExpectedAnswer = source.ExpectedAnswer;
            existing.Intent = source.Intent;
            existing.IssueType = "Good";
            existing.ReviewStatus = "Approved";
            existing.ApprovedForTraining = true;
            existing.ReviewedBy = source.ReviewedBy;
            existing.ReviewedAt = source.ReviewedAt ?? DateTime.UtcNow;
        }

        return imported;
    }

    private void SeedReviewerTrainingExamples()
    {
        var seedPath = ResolvePath("AIChatApp.MLTraining/Data/ReviewerTrainingExamples.json");
        if (!File.Exists(seedPath))
        {
            seedPath = ResolvePath("Data/ReviewerTrainingExamples.json");
        }

        if (!File.Exists(seedPath))
        {
            return;
        }

        var seedFile = JsonSerializer.Deserialize<ReviewerTrainingSeedFile>(
            File.ReadAllText(seedPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (seedFile?.Examples is null)
        {
            return;
        }

        foreach (var group in seedFile.Examples)
        {
            var label = string.IsNullOrWhiteSpace(group.Label) ? "Incorrect" : group.Label.Trim();
            var seedItems = group.Items
                .Where(item =>
                {
                    var answer = string.Equals(label, "Good", StringComparison.OrdinalIgnoreCase)
                        ? item.Answer
                        : item.BadResponse;

                    return !string.IsNullOrWhiteSpace(item.Question) && !string.IsNullOrWhiteSpace(answer);
                })
                .ToList();

            if (seedItems.Count == 0)
            {
                continue;
            }

            var targetCount = Math.Max(seedItems.Count, seedFile.TargetCountPerLabel);
            for (var index = 0; index < targetCount; index++)
            {
                var item = seedItems[index % seedItems.Count];
                var variant = index / seedItems.Count;
                var answer = string.Equals(label, "Good", StringComparison.OrdinalIgnoreCase)
                    ? item.Answer
                    : item.BadResponse;

                _examples.Add(new TrainingExample
                {
                    Id = _nextExampleId++,
                    SourceType = string.Equals(label, "Good", StringComparison.OrdinalIgnoreCase)
                        ? "SeedPublishedKnowledgeEntry"
                        : "SeedReviewedReport",
                    SourceReference = $"Seed-{label}-{index + 1}",
                    Question = BuildSeedQuestionVariant(item.Question, variant),
                    BadResponse = answer.Trim(),
                    ExpectedAnswer = string.Equals(label, "Good", StringComparison.OrdinalIgnoreCase) ? answer.Trim() : string.Empty,
                    Intent = string.IsNullOrWhiteSpace(item.Intent) ? "DocumentationQuestion" : item.Intent.Trim(),
                    IssueType = label,
                    ReviewStatus = "Approved",
                    ApprovedForTraining = true,
                    ReviewedBy = "seed",
                    ReviewedAt = DateTime.UtcNow
                });
            }
        }
    }

    private sealed class ReviewerTrainingSeedFile
    {
        public int TargetCountPerLabel { get; set; } = 50;
        public List<ReviewerTrainingSeedGroup> Examples { get; set; } = [];
    }

    private sealed class ReviewerTrainingSeedGroup
    {
        public string Label { get; set; } = string.Empty;
        public List<ReviewerTrainingSeedItem> Items { get; set; } = [];
    }

    private sealed class ReviewerTrainingSeedItem
    {
        public string Question { get; set; } = string.Empty;
        public string BadResponse { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string Intent { get; set; } = "DocumentationQuestion";
    }

    private static string BuildSeedQuestionVariant(string question, int variant)
    {
        var trimmed = question.Trim();
        if (variant <= 0)
        {
            return trimmed;
        }

        var lower = char.ToLowerInvariant(trimmed[0]) + trimmed[1..];
        return (variant % 5) switch
        {
            1 => $"Can you explain this in AIChatApp: {lower}",
            2 => $"For this project, {lower}",
            3 => $"Please clarify this behavior: {lower}",
            4 => $"In Backoffice and chat, {lower}",
            _ => $"From the documentation, {lower}"
        };
    }
}
