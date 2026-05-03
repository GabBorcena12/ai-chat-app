namespace AIChatApp.MLTraining.Config;

public sealed class TrainingFrontendOptions
{
    public const string SectionName = "TrainingFrontend";

    public string GatewayBaseUrl { get; set; } = "http://localhost:5001/";
    public string ApiClientName { get; set; } = "GajiTechClient";
    public string ApiKey { get; set; } = "dummy-api-key";
    public string ChatAppUrl { get; set; } = "http://localhost:5143/";
    public string BackofficeUrl { get; set; } = "http://localhost:5143/backoffice";
    public string FaqsUrl { get; set; } = "http://localhost:5143/faqs";
}
