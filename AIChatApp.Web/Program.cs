using AIChatApp.Web.Components;
using AIChatApp.Web.Config;
using AIChatApp.Web.Services;
using AIChatApp.Core.Config;
using AIChatApp.MLTraining.Models;
using AIChatApp.MLTraining.Services;
using System;

var builder = WebApplication.CreateBuilder(args);
var allowLocalCredentials = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Docker");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMemoryCache();
builder.Services.AddOptions<FrontendOptions>()
    .Bind(builder.Configuration.GetSection(FrontendOptions.SectionName))
    .Validate(options => Uri.TryCreate(options.GatewayBaseUrl, UriKind.Absolute, out _), "Frontend:GatewayBaseUrl must be an absolute URL.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApiClientName), "Frontend:ApiClientName is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ApiKey)
                   && (allowLocalCredentials
                       || !options.ApiKey.Contains("dummy", StringComparison.OrdinalIgnoreCase)),
        "Frontend:ApiKey must be supplied through environment variables or user secrets.")
    .ValidateOnStart();
builder.Services.Configure<ResponseReviewerOptions>(
    builder.Configuration.GetSection(ResponseReviewerOptions.SectionName));
builder.Services.Configure<AssistantProfileOptions>(options =>
{
    options.ProfileId = "Documentation";
    options.AssistantName = "AI Assistant";
    options.PageTitle = "Documentation Assistant";
    options.SidebarEyebrow = "Project Knowledge Base";
    options.SidebarTitle = "Chat Assistant";
    options.NewChatLabel = "New chat";
    options.WorkspaceEyebrow = "Documentation Workspace";
    options.EmptyStateEyebrow = "Ready when you are";
    options.EmptyStateTitle = "Ask about architecture, setup steps, endpoints, deployment, or any project documentation topic.";
    options.EmptyStateBody = "This workspace is tuned for project documentation help, walkthroughs, onboarding answers, and live streaming responses while you type.";
    options.SignedInMessage = "Signed in. You can now use the documentation workspace.";
    options.AuthRequiredMessage = "Sign in first so the frontend can call the gateway with JWT and API key headers.";
    options.ResponseCompleteStatus = "Response complete.";
    options.AnswerCompleteNotification = "Answer complete.";
    options.ContinuationCompleteNotification = "Continuation complete.";
    options.ReportSavedNotification = "Response report saved.";
    options.HeaderSignedInLabel = "Signed in";
    options.HeaderAuthRequiredLabel = "Auth required";
    options.HeaderAnsweringLabel = "Answering";
    options.HeaderReadyLabel = "Ready";
});
builder.Services.AddHttpClient<AIChatGatewayClient>(client =>
{
    // Streaming chat responses can legitimately exceed the default 100 second HttpClient timeout.
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddScoped<BrowserStorageService>();
builder.Services.AddSingleton<FaqContentService>();
builder.Services.AddSingleton<TrainingWorkspaceService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
