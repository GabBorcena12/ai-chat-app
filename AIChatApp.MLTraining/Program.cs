using AIChatApp.MLTraining.Components;
using AIChatApp.MLTraining.Config;
using AIChatApp.MLTraining.Models;
using AIChatApp.MLTraining.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<ResponseReviewerOptions>(
    builder.Configuration.GetSection(ResponseReviewerOptions.SectionName));
builder.Services.Configure<TrainingFrontendOptions>(
    builder.Configuration.GetSection(TrainingFrontendOptions.SectionName));
builder.Services.AddHttpClient<TrainingAuthService>();
builder.Services.AddScoped<TrainingBrowserStorageService>();
builder.Services.AddSingleton<TrainingWorkspaceService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
