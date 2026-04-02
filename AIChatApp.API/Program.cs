using AIChatApp.API.Services.Generic;
using AIChatApp.API.Services.LLM;
using AIChatApp.API.Services.Orchestration;
using AIChatApp.API.Services.Processing;
using AIChatApp.API.Services.Prompting;
using AIChatApp.Core.Services;
using AIChatApp.Core.Config;
using AIChatApp.Core.Data_Context;
using AIChatApp.Core.Data_Context.Entity;
using AIChatApp.Core.Middleware;
using LLama;
using LLama.Common;
using LLama.Native;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using AIChatApp.Core.Agents;

var builder = WebApplication.CreateBuilder(args);

// Register ChatService
builder.Services.AddControllers();

// Load JWT settings
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"];

// Auth0 Authentication (default scheme)
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer("LocalJwt", options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };
});

// Main App DbContext (for chat history and other core data)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.MigrationsAssembly("AIChatApp.API")
    )
);

// Add Identity (shared with MVC project)
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Inventory App DbContext (for RAG and other features, separate from chat history)
builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("InventoryAppDb")));

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

// Function Executor factory for retries (Scoped)
builder.Services.AddScoped<Func<InteractiveExecutor>>(sp => () =>
{
    var weights = sp.GetRequiredService<LLamaWeights>();
    var paths = new ChatPaths();

    var parameters = new ModelParams(paths.ModelFile)
    {
        ContextSize = 5000
    };

    var newContext = weights.CreateContext(parameters);
        
    return new InteractiveExecutor(newContext);
});
//Email Service
builder.Services.AddScoped<EmailService>();

// Chat history
builder.Services.AddScoped<ChatHistoryService>();

// Original API Chat Service (handles the main flow of processing a chat request)
builder.Services.AddScoped<ApiChatService>();

// LLM Service
builder.Services.AddScoped<ILLMService, LlamaLLMService>();

// Promp Builder like RAG, system context and memory
builder.Services.AddScoped<IPromptBuilder, PromptBuilder>();

// Response processor (includes Agent / keyword logic)
builder.Services.AddScoped<IResponseProcessor, AgentResponseProcessor>();

// Agent tools for product suggestions
builder.Services.AddScoped<AgentTools>();

// Orchestrator
builder.Services.AddScoped<ChatOrchestrator>();

//LLM Service
builder.Services.AddHttpClient<LLMService>();

// JWT Service
builder.Services.AddScoped<JWTServices>();
builder.Services.AddScoped<GoogleAuthenticatorService>();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AI ASSISTANT API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT token as: Bearer {your token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
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
//app.UseMiddleware<IpWhitelistMiddleware>();
app.UseMiddleware<RequestTimingMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseCors("OpenCorsPolicy");
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseAuthentication(); // authenticates the token and sets the User
app.UseAuthorization();  // checks [Authorize] policies
app.MapControllers();

// Auto-apply migrations
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        Console.WriteLine("API: EF Core Migrations applied for AIChatAppDb.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"API: Migration failed: {ex.Message}");
    }
}
app.Run();
