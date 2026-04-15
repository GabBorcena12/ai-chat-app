using AIChatApp.API.Services.Generic;
using AIChatApp.API.Services.LLM;
using AIChatApp.API.Services.Orchestration;
using AIChatApp.API.Services.Processing;
using AIChatApp.API.Services.Prompting;
using AIChatApp.API.Services.Content;
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
builder.Services.Configure<AssistantProfileOptions>(builder.Configuration.GetSection(AssistantProfileOptions.SectionName));
builder.Services.Configure<LocalModelOptions>(builder.Configuration.GetSection(LocalModelOptions.SectionName));
builder.Services.Configure<BackofficeOptions>(builder.Configuration.GetSection(BackofficeOptions.SectionName));
builder.Services.AddSingleton(sp =>
{
    var modelOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalModelOptions>>().Value;
    return new ChatPaths(modelOptions.FileName);
});

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
    var paths = sp.GetRequiredService<ChatPaths>();
    var modelOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalModelOptions>>().Value;

    var parameters = new ModelParams(paths.ModelFile)
    {
        ContextSize = modelOptions.ContextSize
    };

    return LLamaWeights.LoadFromFile(parameters);
});

// Create context per request (Scoped)
// For a fresh brain for every request on Model
builder.Services.AddScoped<LLamaContext>(sp =>
{
    var model = sp.GetRequiredService<LLamaWeights>();
    var paths = sp.GetRequiredService<ChatPaths>();
    var modelOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalModelOptions>>().Value;

    var parameters = new ModelParams(paths.ModelFile)
    {
        ContextSize = modelOptions.ContextSize
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
    var paths = sp.GetRequiredService<ChatPaths>();
    var modelOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalModelOptions>>().Value;

    var parameters = new ModelParams(paths.ModelFile)
    {
        ContextSize = modelOptions.ContextSize
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
builder.Services.AddScoped<IAssistantContentService, AssistantContentService>();

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
        await EnsureRolesAndBackofficeSeedAsync(services);
        Console.WriteLine("API: EF Core Migrations applied for AIChatAppDb.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"API: Migration failed: {ex.Message}");
    }
}
app.Run();

static async Task EnsureRolesAndBackofficeSeedAsync(IServiceProvider services)
{
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var assistantContentService = services.GetRequiredService<IAssistantContentService>();
    var backofficeOptions = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackofficeOptions>>().Value;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("StartupSeed");

    foreach (var roleName in new[] { "User", "AppUser", "DataValidator", "Admin" })
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole(roleName));
        }
    }

    if (backofficeOptions.SeedDefaultAdmin
        && !string.IsNullOrWhiteSpace(backofficeOptions.DefaultAdminUsername)
        && !string.IsNullOrWhiteSpace(backofficeOptions.DefaultAdminEmail)
        && !string.IsNullOrWhiteSpace(backofficeOptions.DefaultAdminPassword))
    {
        var defaultAdmin = await userManager.FindByNameAsync(backofficeOptions.DefaultAdminUsername);
        if (defaultAdmin is null)
        {
            defaultAdmin = new ApplicationUser
            {
                UserName = backofficeOptions.DefaultAdminUsername.Trim(),
                Email = backofficeOptions.DefaultAdminEmail.Trim(),
                EmailConfirmed = true,
                IsConfirmed = true
            };

            var createAdminResult = await userManager.CreateAsync(defaultAdmin, backofficeOptions.DefaultAdminPassword);
            if (!createAdminResult.Succeeded)
            {
                logger.LogWarning("Default admin account could not be created: {Errors}", string.Join("; ", createAdminResult.Errors.Select(error => error.Description)));
            }
            else
            {
                logger.LogInformation("Seeded default admin account {Username}.", defaultAdmin.UserName);
            }
        }

        if (defaultAdmin is not null)
        {
            defaultAdmin.Email ??= backofficeOptions.DefaultAdminEmail.Trim();
            defaultAdmin.EmailConfirmed = true;
            defaultAdmin.IsConfirmed = true;
            defaultAdmin.IsDisabled = false;
            await userManager.UpdateAsync(defaultAdmin);

            if (!await userManager.IsInRoleAsync(defaultAdmin, "Admin"))
            {
                await userManager.AddToRoleAsync(defaultAdmin, "Admin");
            }
        }
    }

    foreach (var username in backofficeOptions.AdminUsernames.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            logger.LogWarning("Backoffice admin username {Username} was not found, so Admin role was not assigned.", username);
            continue;
        }

        if (!await userManager.IsInRoleAsync(user, "Admin"))
        {
            await userManager.AddToRoleAsync(user, "Admin");
        }
    }

    await assistantContentService.SeedProfileContentAsync("Documentation");
    await assistantContentService.SeedProfileContentAsync("AnjeysSupplies");
}
