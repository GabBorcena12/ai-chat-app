using AIChatApp.MLTraining.Models;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace AIChatApp.MLTraining.Services;

public sealed class ResponseReviewerTrainer
{
    private readonly MLContext _ml = new(seed: 7);

    // Trains a small text classifier that predicts answer quality labels such as Good,
    // Incomplete, TooLong, or PromptLeak from reviewed Backoffice examples.
    public ReviewerTrainingResult TrainAndSave(
        IReadOnlyList<TrainingExample> examples,
        string modelPath)
    {
        if (examples.Count == 0)
        {
            throw new InvalidOperationException("The dataset has no approved examples.");
        }

        var trainingRows = BuildTrainingRows(examples);
        var data = _ml.Data.LoadFromEnumerable(trainingRows);
        var split = _ml.Data.TrainTestSplit(data, testFraction: trainingRows.Count >= 6 ? 0.25 : 0.01, seed: 7);

        // Pipeline shape:
        // text -> numeric features -> multiclass classifier -> readable label.
        var pipeline = _ml.Transforms.Conversion.MapValueToKey("Label")
            .Append(_ml.Transforms.Text.FeaturizeText("Features", nameof(ResponseReviewerInput.Text)))
            .Append(_ml.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
            .Append(_ml.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

        var model = pipeline.Fit(split.TrainSet);
        var predictions = model.Transform(trainingRows.Count >= 6 ? split.TestSet : split.TrainSet);
        var metrics = _ml.MulticlassClassification.Evaluate(predictions);

        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        _ml.Model.Save(model, split.TrainSet.Schema, modelPath);

        return new ReviewerTrainingResult
        {
            ModelPath = modelPath,
            Accuracy = metrics.MicroAccuracy,
            F1Score = metrics.MacroAccuracy,
            ExampleCount = examples.Count,
            LabelCount = trainingRows.Select(row => row.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count()
        };
    }

    private static List<ResponseReviewerInput> BuildTrainingRows(IReadOnlyList<TrainingExample> examples)
    {
        // BadResponse rows teach the reviewer what problem category to detect.
        var rows = examples.Select(example => new ResponseReviewerInput
        {
            Text = ResponseReviewerService.BuildFeatureText(example.Question, example.BadResponse, example.Intent),
            Label = string.IsNullOrWhiteSpace(example.IssueType) ? "Incorrect" : example.IssueType
        }).ToList();

        // A reviewer needs "Good" examples too, otherwise it can learn that every answer is bad.
        rows.AddRange(examples
            .Where(example => !string.IsNullOrWhiteSpace(example.ExpectedAnswer))
            .Select(example => new ResponseReviewerInput
            {
                Text = ResponseReviewerService.BuildFeatureText(example.Question, example.ExpectedAnswer, example.Intent),
                Label = "Good"
            }));

        return rows;
    }
}

public sealed class ReviewerTrainingResult
{
    public string ModelPath { get; set; } = string.Empty;
    public double Accuracy { get; set; }
    public double F1Score { get; set; }
    public int ExampleCount { get; set; }
    public int LabelCount { get; set; }
}
