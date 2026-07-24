using AIChatApp.MLTraining.Models;
using AIChatApp.MLTraining.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIChatApp.Tests.MLTraining;

public sealed class ResponseReviewerQualityTests : IDisposable
{
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), $"aichatapp-reviewer-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ReviewWithRules_ShouldFlagShortOrIncompleteResponses()
    {
        var result = ResponseReviewerService.ReviewWithRules(
            "How do I configure the gateway locally?",
            "Use the",
            "DocumentationQuestion");

        Assert.Equal("Incomplete", result.IssueType);
        Assert.True(result.IsRisky);
        Assert.Equal("Rules", result.Source);
    }

    [Fact]
    public void ReviewWithRules_ShouldAcceptClearCompleteResponses()
    {
        var result = ResponseReviewerService.ReviewWithRules(
            "How do I configure the gateway locally?",
            "Set the web app gateway URL to the local API gateway address and include the configured API key header.",
            "DocumentationQuestion");

        Assert.Equal("Good", result.IssueType);
        Assert.False(result.IsRisky);
        Assert.Equal("Rules", result.Source);
    }

    [Fact]
    public void ReviewWithRules_ShouldFlagMlTrainingContradictionAsIncorrect()
    {
        var result = ResponseReviewerService.ReviewWithRules(
            "How does ML Training improve future chat answers in AIChatApp?",
            "ML Training doesn't improve future chat answers in AIChatApp. It's used for training models that will be used by the app. The app uses a pre-trained model for all chat responses.",
            "DocumentationQuestion");

        Assert.Equal("Incorrect", result.IssueType);
        Assert.True(result.IsRisky);
        Assert.Equal("Rules", result.Source);
    }

    [Fact]
    public void PublishedModel_ShouldClassifyKnownReviewedBadResponse()
    {
        var publishedModelPath = Path.Combine(_tempFolder, "published-response-reviewer.zip");
        var trainer = new ResponseReviewerTrainer();
        var examples = BuildQualityExamples();

        trainer.TrainAndSave(examples, publishedModelPath);

        var reviewer = new ResponseReviewerService(Options.Create(new ResponseReviewerOptions
        {
            Enabled = true,
            PublishedModelPath = publishedModelPath
        }));

        var result = reviewer.Review(
            "How do I configure the gateway locally?",
            "The gateway works by magic and there is nothing to configure.",
            "DocumentationQuestion");

        Assert.Equal("ML.NET", result.Source);
        Assert.Equal("Incorrect", result.IssueType);
        Assert.True(result.Confidence > 0);
        Assert.True(result.IsRisky);
    }

    [Fact]
    public async Task TrainingWorkspace_ShouldPublishModelFileThatReviewerCanLoad()
    {
        var options = Options.Create(new ResponseReviewerOptions
        {
            Enabled = true,
            CandidateModelFolder = Path.Combine(_tempFolder, "candidates"),
            PublishedModelPath = Path.Combine(_tempFolder, "published-response-reviewer.zip")
        });
        var workspace = new TrainingWorkspaceService(options);

        workspace.ImportApprovedExamples(BuildQualityExamples());
        var dataset = workspace.BuildDataset("DocumentationQualityReviewer");
        await workspace.QueueAndRunTrainingAsync(dataset.Id, "unit-test", CancellationToken.None);
        workspace.PublishModel(workspace.LatestModel!.Id);

        Assert.True(File.Exists(options.Value.PublishedModelPath));

        var reviewer = new ResponseReviewerService(options);
        var result = reviewer.Review(
            "Where are prompt templates stored?",
            "Prompt templates are hardcoded in the UI and cannot be changed.",
            "DocumentationQuestion");

        Assert.Equal("ML.NET", result.Source);
        Assert.Equal("Incorrect", result.IssueType);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, recursive: true);
        }
    }

    private static List<TrainingExample> BuildQualityExamples()
        =>
        [
            CreateExample(
                "How do I configure the gateway locally?",
                "The gateway works by magic and there is nothing to configure.",
                "Set the Web app gateway URL to the local API gateway address and include the configured API key header.",
                "GatewayQuestion"),
            CreateExample(
                "Where are prompt templates stored?",
                "Prompt templates are hardcoded in the UI and cannot be changed.",
                "Prompt templates are stored in the database with file-based fallback templates for the Documentation profile.",
                "DocumentationQuestion"),
            CreateExample(
                "How does 2FA work?",
                "2FA is not supported in this project.",
                "The app supports Google Authenticator setup through the account security flow and verifies codes during sign in.",
                "AuthQuestion"),
            CreateExample(
                "What does the ML Training workflow do?",
                "ML Training writes final chat answers directly.",
                "ML Training builds a response-quality reviewer that checks generated answers and helps trigger repair when needed.",
                "MLTrainingQuestion"),
            CreateExample(
                "How are reported responses used?",
                "Reported responses are deleted after review.",
                "Approved reported responses become training examples for the response-quality reviewer and may also be promoted to knowledge.",
                "MLTrainingQuestion"),
            CreateExample(
                "Where is the published reviewer stored?",
                "The published reviewer is stored in browser local storage.",
                "The published reviewer is saved as a ML.NET model zip at the configured PublishedModelPath.",
                "MLTrainingQuestion"),
            CreateExample(
                "How does the chat app use knowledge entries?",
                "Knowledge entries are ignored by the assistant.",
                "Published knowledge entries are loaded into assistant content so the prompt can include reusable project facts.",
                "DocumentationQuestion"),
            CreateExample(
                "What should a validated response contain?",
                "It should be short.",
                "It should contain the corrected answer that should have been returned, with enough detail to reuse for review or knowledge.",
                "DocumentationQuestion")
        ];

    private static TrainingExample CreateExample(string question, string badResponse, string expectedAnswer, string intent)
        => new()
        {
            SourceType = "ReviewedReport",
            SourceReference = $"Test-{Guid.NewGuid():N}",
            Question = question,
            BadResponse = badResponse,
            ExpectedAnswer = expectedAnswer,
            IssueType = "Incorrect",
            Intent = intent,
            ReviewStatus = "Approved",
            ApprovedForTraining = true,
            ReviewedBy = "unit-test",
            ReviewedAt = DateTime.UtcNow
        };
}
