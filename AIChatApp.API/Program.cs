using AIChatApp.API.Middleware;
using AIChatApp.API.Service;
using AIChatApp.Core.Config;
using AIChatApp.Core.Data;
using LLama;
using LLama.Common;
using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore.SqlServer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Register ChatService
builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<InteractiveExecutor>(sp =>
{
    var paths = new ChatPaths();

    var parameters = new ModelParams(paths.ModelFile)
    {
        ContextSize = 5000
    };

    var model = LLamaWeights.LoadFromFile(parameters);
    var context = model.CreateContext(parameters);

    return new InteractiveExecutor(context);
});

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

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}
// Middleware
app.UseMiddleware<ApiExceptionMiddleware>();

app.UseCors("OpenCorsPolicy");
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();