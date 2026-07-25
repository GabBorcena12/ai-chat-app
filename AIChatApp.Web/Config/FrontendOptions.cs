namespace AIChatApp.Web.Config;

public class FrontendOptions
{
    public const string SectionName = "Frontend";
    public string GatewayBaseUrl { get; set; } = "http://localhost:5001/";
    public string ApiClientName { get; set; } = "GajiTechClient";
    public string ApiKey { get; set; } = "dummy-api-key";
}
