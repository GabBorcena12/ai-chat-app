using System.Text;
using AIChatApp.MLTraining.Services;
using Xunit;

namespace AIChatApp.Tests.MLTraining;

public class TrainingWorkflowTests
{
    [Fact]
    public void SeedData_ShouldRepresentReviewedReports_NotRawTraining()
    {
        var workspace = new TrainingWorkspaceService();

        Assert.All(workspace.Examples, example =>
        {
            Assert.Equal("ReviewedReport", example.SourceType);
            Assert.Equal("Reviewed", example.ReviewStatus);
            Assert.False(example.ApprovedForTraining);
            Assert.False(string.IsNullOrWhiteSpace(example.Question));
            Assert.False(string.IsNullOrWhiteSpace(example.ExpectedAnswer));
            Assert.False(string.IsNullOrWhiteSpace(example.Intent));
            Assert.False(string.IsNullOrWhiteSpace(example.IssueType));
        });
    }

    [Fact]
    public void ApproveExample_ShouldMarkExampleAsTrainingReady()
    {
        var workspace = new TrainingWorkspaceService();
        var example = workspace.Examples.First();

        workspace.ApproveExample(example.Id, "validator");

        Assert.True(example.ApprovedForTraining);
        Assert.Equal("Approved", example.ReviewStatus);
        Assert.Equal("validator", example.ReviewedBy);
        Assert.NotNull(example.ReviewedAt);
    }

    [Fact]
    public void BuildDataset_ShouldOnlyCountApprovedExamples()
    {
        var workspace = new TrainingWorkspaceService();
        var first = workspace.Examples.First();

        workspace.ApproveExample(first.Id, "validator");
        var dataset = workspace.BuildDataset("DocumentationQuality");

        Assert.Equal(1, dataset.ExampleCount);
        Assert.Equal(1, dataset.Version);
    }

    [Fact]
    public async Task QueueAndRunTrainingAsync_ShouldRejectDatasetWithoutApprovedExamples()
    {
        var workspace = new TrainingWorkspaceService();
        var dataset = workspace.BuildDataset("DocumentationQuality");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workspace.QueueAndRunTrainingAsync(dataset.Id, "trainer", CancellationToken.None));

        Assert.Equal("The dataset has no approved examples.", ex.Message);
    }

    [Fact]
    public async Task QueueAndRunTrainingAsync_ShouldCreateCompletedJobAndModelVersion()
    {
        var workspace = new TrainingWorkspaceService();
        foreach (var example in workspace.Examples)
        {
            workspace.ApproveExample(example.Id, "validator");
        }

        var dataset = workspace.BuildDataset("DocumentationQuality");
        var job = await workspace.QueueAndRunTrainingAsync(dataset.Id, "trainer", CancellationToken.None);

        Assert.Equal("Completed", job.Status);
        Assert.NotNull(job.Accuracy);
        Assert.NotNull(job.F1Score);
        Assert.Contains(workspace.Models, model => model.TrainingJobId == job.Id);
    }

    [Fact]
    public async Task ImportTextFileAsync_ShouldCreateDraftExamplesThatRequireReview()
    {
        var workspace = new TrainingWorkspaceService();
        var text = "Gateway requests need API keys.\n\nDocker containers should use service names on the Docker network.";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var count = await workspace.ImportTextFileAsync(stream, "notes.txt", CancellationToken.None);

        Assert.Equal(2, count);
        var imported = workspace.Examples.Where(example => example.SourceType == "UploadedText").ToList();
        Assert.Equal(2, imported.Count);
        Assert.All(imported, example =>
        {
            Assert.Equal("Draft", example.ReviewStatus);
            Assert.False(example.ApprovedForTraining);
            Assert.Equal("NeedsLabel", example.Intent);
            Assert.Equal("NeedsLabel", example.IssueType);
        });
    }

    [Fact]
    public async Task PublishModel_ShouldKeepOnlyOnePublishedVersion()
    {
        var workspace = new TrainingWorkspaceService();
        foreach (var example in workspace.Examples)
        {
            workspace.ApproveExample(example.Id, "validator");
        }

        var firstDataset = workspace.BuildDataset("DocumentationQuality");
        var firstJob = await workspace.QueueAndRunTrainingAsync(firstDataset.Id, "trainer", CancellationToken.None);
        var secondDataset = workspace.BuildDataset("DocumentationQuality");
        var secondJob = await workspace.QueueAndRunTrainingAsync(secondDataset.Id, "trainer", CancellationToken.None);
        var secondModel = workspace.Models.First(model => model.TrainingJobId == secondJob.Id);

        workspace.PublishModel(secondModel.Id);

        Assert.Single(workspace.Models, model => model.IsPublished);
        Assert.True(secondModel.IsPublished);
        Assert.False(workspace.Models.First(model => model.TrainingJobId == firstJob.Id).IsPublished);
    }
}
