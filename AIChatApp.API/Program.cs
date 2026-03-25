using AIChatApp.API.Service;
using AIChatApp.Core.Config;
using AIChatApp.Core.Data;
using AIChatApp.Gateway.Middleware;
using LLama;
using LLama.Common;
using LLama.Native;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Register ChatService
builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Load or Extract model package once (Singleton)
builder.Services.AddSingleton<LLamaWeights>(sp =>
{
    var paths = new ChatPaths();

    var parameters = new ModelParams(paths.ModelFile)
    {
        ContextSize = 5000
    };

    return LLamaWeights.LoadFromFile(parameters);
});

// Create context per request (Scoped)
// For a fresh brain for every request on Model
builder.Services.AddScoped<LLamaContext>(sp =>
{
    var model = sp.GetRequiredService<LLamaWeights>();
    var paths = new ChatPaths();

    var parameters = new ModelParams(paths.ModelFile)
    {
        ContextSize = 5000
    };

    return model.CreateContext(parameters);
});

// Executor per request (Scoped)
builder.Services.AddScoped<InteractiveExecutor>(sp =>
{
    var context = sp.GetRequiredService<LLamaContext>();
    return new InteractiveExecutor(context);
});
builder.Services.AddScoped<ChatHistoryService>();
builder.Services.AddScoped<ApiChatService>();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "GAJI AI API", Version = "v1" });
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("OpenCorsPolicy", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Database configuration
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Remove EF Core logs and keep chat app logs (including Info)
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Error);
builder.Logging.AddFilter("AIChatApp", LogLevel.Information);

var app = builder.Build();

// Silence all llama.cpp logs
NativeLibraryConfig.All.WithLogCallback((level, message) => { });

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}
// Middleware
app.UseMiddleware<IpWhitelistMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseCors("OpenCorsPolicy");
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Auto-apply migrations
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        Console.WriteLine("✅ API: EF Core Migrations applied for AIChatAppDb.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ API: Migration failed: {ex.Message}");
    }
}
app.Run();