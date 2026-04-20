namespace AIChatApp.Core.Config;

public class AssistantProfileOptions
{
    public const string SectionName = "AssistantProfile";

    public string ProfileId { get; set; } = "Documentation";
    public string AssistantName { get; set; } = "AI Assistant";
    public string PageTitle { get; set; } = "Documentation Assistant";
    public string SidebarEyebrow { get; set; } = "Project Knowledge Base";
    public string SidebarTitle { get; set; } = "Chat Assistant";
    public string NewChatLabel { get; set; } = "New chat";
    public string WorkspaceEyebrow { get; set; } = "Documentation Workspace";
    public string EmptyStateEyebrow { get; set; } = "Ready when you are";
    public string EmptyStateTitle { get; set; } = "Ask about architecture, setup steps, endpoints, deployment, or any project documentation topic.";
    public string EmptyStateBody { get; set; } = "This workspace is tuned for project documentation help, walkthroughs, onboarding answers, and live streaming responses while you type.";
    public string SignedInMessage { get; set; } = "Signed in. You can now use the documentation workspace.";
    public string AuthRequiredMessage { get; set; } = "Sign in first so the frontend can call the gateway with JWT and API key headers.";
    public string ResponseCompleteStatus { get; set; } = "Response complete.";
    public string AnswerCompleteNotification { get; set; } = "Answer complete.";
    public string ContinuationCompleteNotification { get; set; } = "Continuation complete.";
    public string ReportSavedNotification { get; set; } = "Response report saved.";
    public string HeaderSignedInLabel { get; set; } = "Signed in";
    public string HeaderAuthRequiredLabel { get; set; } = "Auth required";
    public string HeaderAnsweringLabel { get; set; } = "Answering";
    public string HeaderReadyLabel { get; set; } = "Ready";
}
