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
using AIChatApp.MLTraining.Models;
using AIChatApp.MLTraining.Services;
using LLama;
using LLama.Common;
using LLama.Native;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
using AIChatApp.Core.Agents;

var builder = WebApplication.CreateBuilder(args);

// Register ChatService
builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.Configure<AssistantProfileOptions>(builder.Configuration.GetSection(AssistantProfileOptions.SectionName));
builder.Services.Configure<LocalModelOptions>(builder.Configuration.GetSection(LocalModelOptions.SectionName));
builder.Services.Configure<BackofficeOptions>(builder.Configuration.GetSection(BackofficeOptions.SectionName));
builder.Services.Configure<ResponseReviewerOptions>(builder.Configuration.GetSection(ResponseReviewerOptions.SectionName));
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
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("LocalJwt");

            logger.LogWarning(context.Exception, "JWT authentication failed for {Path}.", context.HttpContext.Request.Path);
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("LocalJwt");

            logger.LogWarning(
                "JWT challenge for {Path}. Error: {Error}. Description: {Description}.",
                context.HttpContext.Request.Path,
                context.Error,
                context.ErrorDescription);

            return Task.CompletedTask;
        }
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
builder.Services.AddScoped<CoreDataImportService>();

// Response processor (includes Agent / keyword logic)
builder.Services.AddScoped<IResponseProcessor, AgentResponseProcessor>();
builder.Services.AddSingleton<ResponseReviewerService>();
builder.Services.AddSingleton<IResponseReviewer>(sp => sp.GetRequiredService<ResponseReviewerService>());
builder.Services.AddSingleton<TrainingWorkspaceService>();

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

    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document, null),
            new List<string>()
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
        await EnsureCoreDataFilesTableAsync(db);
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
    var coreDataImportService = services.GetRequiredService<CoreDataImportService>();
    var backofficeOptions = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackofficeOptions>>().Value;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("StartupSeed");

    foreach (var roleName in new[]
    {
        AppRoleNames.User,
        AppRoleNames.Validator,
        AppRoleNames.Admin,
        AppRoleNames.LegacyAppUser,
        AppRoleNames.LegacyDataValidator
    })
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole(roleName));
        }
    }

    if (backofficeOptions.SeedDefaultRoleAccounts || backofficeOptions.SeedDefaultAdmin)
    {
        await EnsureDefaultRoleAccountAsync(
            userManager,
            logger,
            backofficeOptions.DefaultAdminUsername,
            backofficeOptions.DefaultAdminEmail,
            backofficeOptions.DefaultAdminPassword,
            AppRoleNames.Admin);

        await EnsureDefaultRoleAccountAsync(
            userManager,
            logger,
            backofficeOptions.DefaultUserUsername,
            backofficeOptions.DefaultUserEmail,
            backofficeOptions.DefaultUserPassword,
            AppRoleNames.User);

        await EnsureDefaultRoleAccountAsync(
            userManager,
            logger,
            backofficeOptions.DefaultValidatorUsername,
            backofficeOptions.DefaultValidatorEmail,
            backofficeOptions.DefaultValidatorPassword,
            AppRoleNames.Validator);
    }

    foreach (var username in backofficeOptions.AdminUsernames.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            logger.LogWarning("Backoffice admin username {Username} was not found, so Admin role was not assigned.", username);
            continue;
        }

        if (!await userManager.IsInRoleAsync(user, AppRoleNames.Admin))
        {
            await userManager.AddToRoleAsync(user, AppRoleNames.Admin);
        }
    }

    await coreDataImportService.ImportCoreDataJsonAsync();
    await assistantContentService.SeedProfileContentAsync("Documentation");
}

static async Task EnsureDefaultRoleAccountAsync(
    UserManager<ApplicationUser> userManager,
    ILogger logger,
    string username,
    string email,
    string password,
    string roleName)
{
    if (string.IsNullOrWhiteSpace(username)
        || string.IsNullOrWhiteSpace(email)
        || string.IsNullOrWhiteSpace(password))
    {
        return;
    }

    var normalizedUsername = username.Trim();
    var normalizedEmail = email.Trim();
    var user = await userManager.FindByNameAsync(normalizedUsername)
        ?? await userManager.FindByEmailAsync(normalizedEmail);

    if (user is null)
    {
        user = new ApplicationUser
        {
            UserName = normalizedUsername,
            Email = normalizedEmail,
            EmailConfirmed = true,
            IsConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            logger.LogWarning(
                "Default {RoleName} account could not be created: {Errors}",
                roleName,
                string.Join("; ", createResult.Errors.Select(error => error.Description)));
            return;
        }

        logger.LogInformation("Seeded default {RoleName} account {Username}.", roleName, user.UserName);
    }

    user.Email ??= normalizedEmail;
    user.EmailConfirmed = true;
    user.IsConfirmed = true;
    user.IsDisabled = false;
    await userManager.UpdateAsync(user);

    if (!await userManager.IsInRoleAsync(user, roleName))
    {
        await userManager.AddToRoleAsync(user, roleName);
    }
}

static async Task EnsureCoreDataFilesTableAsync(AppDbContext db)
{
    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID(N'[dbo].[CoreDataFiles]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[CoreDataFiles] (
                [Id] int NOT NULL IDENTITY,
                [RelativePath] nvarchar(450) NOT NULL,
                [ContentKey] nvarchar(450) NOT NULL,
                [Area] nvarchar(max) NOT NULL,
                [ProfileId] nvarchar(max) NULL,
                [ContentType] nvarchar(max) NOT NULL,
                [FileName] nvarchar(max) NOT NULL,
                [RawJson] nvarchar(max) NOT NULL,
                [Content] nvarchar(max) NULL,
                [StructuredJson] nvarchar(max) NULL,
                [IsPublished] bit NOT NULL,
                [CreatedAt] datetime2 NOT NULL,
                [UpdatedAt] datetime2 NOT NULL,
                CONSTRAINT [PK_CoreDataFiles] PRIMARY KEY ([Id])
            );
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE [name] = N'IX_CoreDataFiles_ContentKey'
              AND [object_id] = OBJECT_ID(N'[dbo].[CoreDataFiles]')
        )
        BEGIN
            CREATE INDEX [IX_CoreDataFiles_ContentKey]
            ON [dbo].[CoreDataFiles] ([ContentKey]);
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE [name] = N'IX_CoreDataFiles_RelativePath'
              AND [object_id] = OBJECT_ID(N'[dbo].[CoreDataFiles]')
        )
        BEGIN
            CREATE UNIQUE INDEX [IX_CoreDataFiles_RelativePath]
            ON [dbo].[CoreDataFiles] ([RelativePath]);
        END;
        """);
}
