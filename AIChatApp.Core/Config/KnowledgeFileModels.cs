namespace AIChatApp.Core.Config;

public sealed class JsonContentFile
{
    public string Content { get; set; } = string.Empty;
}

public sealed class JsonStringListFile
{
    public List<string> Items { get; set; } = [];
}

public sealed class JsonQuickAnswersFile
{
    public List<JsonQuickAnswerEntry> Entries { get; set; } = [];
}

public sealed class JsonQuickAnswerEntry
{
    public string Title { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public List<string> Keywords { get; set; } = [];
    public string SourceName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

public sealed class JsonTopicKnowledgeFile
{
    public List<JsonTopicEntry> Topics { get; set; } = [];
}

public sealed class JsonTopicEntry
{
    public string Topic { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
    public List<string> Context { get; set; } = [];
}
