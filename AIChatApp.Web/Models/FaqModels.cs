namespace AIChatApp.Web.Models;

public sealed class FaqItemViewModel
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
}

public sealed class FaqTopicViewModel
{
    public string Topic { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = [];
}
