using AIChatApp.MLTraining.Models;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace AIChatApp.MLTraining.Services;

public sealed class ResponseReviewerTrainer
{
    private readonly MLContext _ml = new(seed: 7);

    // Trains the ML.NET response-quality reviewer and writes the candidate model ZIP.
    // Step 1: Convert approved TrainingExample records into labeled text rows.
    //         - Published knowledge examples become Good rows.
    //         - Approved reported responses keep their issue label, such as Incorrect,
    //           Incomplete, TooLong, PromptLeak, Repetitive, OffTopic, or Other.
    // Step 2: Confirm the dataset has at least two labels so the classifier can learn
    //         the difference between acceptable and risky answers.
    // Step 3: Build the ML.NET pipeline: label -> key, text -> numeric features,
    //         SDCA multiclass classifier, predicted key -> readable label.
    // Step 4: Use a holdout validation split only when there is enough balanced data;
    //         otherwise train on all rows and skip misleading Accuracy/F1 metrics.
    // Step 5: Save the trained candidate model to modelPath. Publishing later copies
    //         this ZIP to the configured runtime reviewer path.
    public ReviewerTrainingResult TrainAndSave(
        IReadOnlyList<TrainingExample> examples,
        string modelPath)
    {
        if (examples.Count == 0)
        {
            throw new InvalidOperationException("The dataset has no approved examples.");
        }

        var trainingRows = BuildTrainingRows(examples);
        var labelCount = trainingRows
            .Select(row => row.Label)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (labelCount < 2)
        {
            throw new InvalidOperationException("The reviewer needs at least one issue label and one Good answer example before training.");
        }

        var data = _ml.Data.LoadFromEnumerable(trainingRows);
        var useHoldoutMetrics = CanUseHoldoutMetrics(trainingRows);

        // Pipeline shape:
        // text -> numeric features -> multiclass classifier -> readable label.
        var pipeline = _ml.Transforms.Conversion.MapValueToKey("Label")
            .Append(_ml.Transforms.Text.FeaturizeText("Features", nameof(ResponseReviewerInput.Text)))
            .Append(_ml.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
            .Append(_ml.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

        var trainSet = data;
        IDataView? testSet = null;
        if (useHoldoutMetrics)
        {
            var split = _ml.Data.TrainTestSplit(data, testFraction: 0.25, seed: 7);
            trainSet = split.TrainSet;
            testSet = split.TestSet;
        }

        var model = pipeline.Fit(trainSet);

        double? accuracy = null;
        double? f1Score = null;
        var metricNote = "Model trained. Validation metrics were skipped because the dataset is still small; add more approved examples for reliable accuracy and F1.";

        if (useHoldoutMetrics && testSet is not null)
        {
            var predictions = model.Transform(testSet);
            var metrics = _ml.MulticlassClassification.Evaluate(predictions);
            accuracy = metrics.MicroAccuracy;
            f1Score = metrics.MacroAccuracy;
            metricNote = "Model trained and evaluated with a holdout validation split.";
        }

        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        _ml.Model.Save(model, trainSet.Schema, modelPath);

        return new ReviewerTrainingResult
        {
            ModelPath = modelPath,
            Accuracy = accuracy,
            F1Score = f1Score,
            ExampleCount = examples.Count,
            LabelCount = labelCount,
            MetricNote = metricNote
        };
    }

    private static List<ResponseReviewerInput> BuildTrainingRows(IReadOnlyList<TrainingExample> examples)
    {
        return examples
            .Select(example =>
            {
                var label = string.IsNullOrWhiteSpace(example.IssueType) ? "Incorrect" : example.IssueType.Trim();
                var answer = string.Equals(label, "Good", StringComparison.OrdinalIgnoreCase)
                    ? FirstNonEmpty(example.ExpectedAnswer, example.BadResponse)
                    : example.BadResponse;

                return new ResponseReviewerInput
                {
                    Text = ResponseReviewerService.BuildFeatureText(example.Question, answer, example.Intent),
                    Label = label
                };
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.Text))
            .ToList();
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool CanUseHoldoutMetrics(IReadOnlyList<ResponseReviewerInput> rows)
    {
        if (rows.Count < 20)
        {
            return false;
        }

        return rows
            .GroupBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .All(group => group.Count() >= 4);
    }
}

public sealed class ReviewerTrainingResult
{
    public string ModelPath { get; set; } = string.Empty;
    public double? Accuracy { get; set; }
    public double? F1Score { get; set; }
    public int ExampleCount { get; set; }
    public int LabelCount { get; set; }
    public string MetricNote { get; set; } = string.Empty;
}
