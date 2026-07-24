using AIChatApp.API.Model;
using AIChatApp.Core.Config;
using AIChatApp.Core.Data_Context;
using AIChatApp.Core.Data_Context.Entity;
using AIChatApp.MLTraining.Models;
using AIChatApp.MLTraining.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace AIChatApp.API.Controllers
{
    [ApiController]
    [Route("api/backoffice")]
    [Authorize(AuthenticationSchemes = "LocalJwt", Roles = "Admin")]
    public class BackofficeController : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly AppDbContext _dbContext;
        private readonly ChatPaths _chatPaths;
        private readonly ILogger<BackofficeController> _logger;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly TrainingWorkspaceService _trainingWorkspace;

        public BackofficeController(
            AppDbContext dbContext,
            ChatPaths chatPaths,
            ILogger<BackofficeController> logger,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            TrainingWorkspaceService trainingWorkspace)
        {
            _dbContext = dbContext;
            _chatPaths = chatPaths;
            _logger = logger;
            _userManager = userManager;
            _roleManager = roleManager;
            _trainingWorkspace = trainingWorkspace;
        }

        [HttpGet("reports")]
        public async Task<IActionResult> GetReports([FromQuery] string? status = null)
        {
            var query = _dbContext.ChatResponseReports.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => x.ReviewStatus == status);
            }

            var reports = await query
                .OrderByDescending(x => x.CreatedAt)
                .Take(200)
                .ToListAsync();

            return Ok(reports);
        }

        [HttpGet("workflow-summary")]
        public async Task<IActionResult> GetWorkflowSummary()
        {
            var reports = await _dbContext.ChatResponseReports
                .AsNoTracking()
                .ToListAsync();

            var publishedKnowledgeCount = await _dbContext.AssistantKnowledgeEntries
                .AsNoTracking()
                .CountAsync(x => x.ProfileId == "Documentation" && x.IsPublished);

            return Ok(new BackofficeWorkflowSummaryViewModel
            {
                PendingReports = reports.Count(x => string.Equals(x.ReviewStatus, "Pending", StringComparison.OrdinalIgnoreCase)),
                ReviewedReports = reports.Count(x => string.Equals(x.ReviewStatus, "Reviewed", StringComparison.OrdinalIgnoreCase)),
                ApprovedReports = reports.Count(x => string.Equals(x.ReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase)),
                TrainingCandidates = reports.Count(IsTrainingCandidate),
                PublishedKnowledgeEntries = publishedKnowledgeCount
            });
        }

        [HttpGet("training-candidates")]
        public async Task<IActionResult> GetTrainingCandidates()
        {
            var candidates = await _dbContext.ChatResponseReports
                .AsNoTracking()
                .Where(x => x.ReviewStatus == "Approved"
                    && x.ValidatedResponse != null
                    && x.ValidatedResponse != string.Empty
                    && x.ReviewCategory != null
                    && x.ReviewCategory != string.Empty)
                .OrderByDescending(x => x.ReviewedAt ?? x.CreatedAt)
                .Take(200)
                .Select(x => new TrainingCandidateViewModel
                {
                    ReportId = x.Id,
                    Question = x.ValidatedQuestion ?? x.UserPrompt,
                    BadResponse = x.AssistantResponse,
                    CorrectAnswer = x.ValidatedResponse ?? string.Empty,
                    IssueType = x.ReviewCategory ?? "Other",
                    Intent = x.ContextMode ?? "DocumentationQuestion",
                    ReviewedBy = x.ReviewedBy ?? string.Empty,
                    ReviewedAt = x.ReviewedAt,
                    IsPromotedToKnowledge = x.PromotedKnowledgeEntryId.HasValue
                })
                .ToListAsync();

            return Ok(candidates);
        }

        [HttpGet("reviewer/state")]
        public IActionResult GetReviewerState()
            => Ok(_trainingWorkspace.GetState());

        [HttpPost("reviewer/build-dataset")]
        public async Task<IActionResult> BuildReviewerDataset()
        {
            var candidates = await LoadTrainingCandidateEntitiesAsync();
            var imported = _trainingWorkspace.ImportApprovedExamples(candidates.Select(ToTrainingExample));
            var dataset = _trainingWorkspace.BuildDataset("DocumentationQualityReviewer");

            return Ok($"Dataset v{dataset.Version} built with {dataset.ExampleCount} approved example(s). Imported {imported} new candidate(s).");
        }

        [HttpPost("reviewer/train")]
        public async Task<IActionResult> TrainReviewer(CancellationToken cancellationToken)
        {
            var dataset = _trainingWorkspace.LatestDataset;
            if (dataset is null)
            {
                return BadRequest("Build a reviewer dataset before training.");
            }

            var job = await _trainingWorkspace.QueueAndRunTrainingAsync(
                dataset.Id,
                User.FindFirstValue(ClaimTypes.Name) ?? "admin",
                cancellationToken);

            return Ok($"ML.NET reviewer trained. Accuracy: {job.Accuracy:P1}, F1: {job.F1Score:P1}.");
        }

        [HttpPost("reviewer/publish-latest")]
        public IActionResult PublishLatestReviewer()
        {
            var model = _trainingWorkspace.LatestModel;
            if (model is null)
            {
                return BadRequest("Train a reviewer model before publishing.");
            }

            _trainingWorkspace.PublishModel(model.Id);
            return Ok($"Published reviewer model {model.Version}. The API can now use it to review responses.");
        }

        [HttpPut("reports/{id:int}/review")]
        public async Task<IActionResult> ReviewReport(int id, [FromBody] ReviewReportedResponseRequest request)
        {
            var report = await _dbContext.ChatResponseReports.FirstOrDefaultAsync(x => x.Id == id);
            if (report is null)
            {
                return NotFound("Report not found.");
            }

            report.ReviewStatus = string.IsNullOrWhiteSpace(request.ReviewStatus) ? "Reviewed" : request.ReviewStatus.Trim();
            report.ReviewCategory = request.ReviewCategory?.Trim();
            report.ReviewNotes = request.ReviewNotes?.Trim();
            report.ValidatedQuestion = request.ValidatedQuestion?.Trim();
            report.ValidatedResponse = request.ValidatedResponse?.Trim();
            report.ReviewedBy = User.FindFirstValue(ClaimTypes.Name) ?? "admin";
            report.ReviewedAt = DateTime.UtcNow;

            if (request.PromoteToKnowledge)
            {
                var knowledgeEntry = BuildKnowledgeEntryFromReview(report, request);
                _dbContext.AssistantKnowledgeEntries.Add(knowledgeEntry);
                await _dbContext.SaveChangesAsync();
                report.PromotedKnowledgeEntryId = knowledgeEntry.Id;
            }

            await _dbContext.SaveChangesAsync();
            return Ok("Report review saved.");
        }

        [HttpGet("prompt-templates")]
        public async Task<IActionResult> GetPromptTemplates([FromQuery] string profileId = "Documentation")
        {
            try
            {
                var templates = await _dbContext.AssistantPromptTemplates
                    .AsNoTracking()
                    .Where(x => x.ProfileId == profileId)
                    .OrderBy(x => x.TemplateName)
                    .ToListAsync();

                if (templates.Count > 0)
                {
                    return Ok(templates);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to load prompt templates from the database for profile {ProfileId}. Falling back to file-backed defaults.", profileId);
            }

            return Ok(BuildFallbackPromptTemplates(profileId));
        }

        [HttpPut("prompt-templates/{id:int}")]
        public async Task<IActionResult> UpdatePromptTemplate(int id, [FromBody] SavePromptTemplateRequest request)
        {
            var template = await _dbContext.AssistantPromptTemplates.FirstOrDefaultAsync(x => x.Id == id);
            if (template is null)
            {
                return NotFound("Prompt template not found.");
            }

            template.ProfileId = request.ProfileId.Trim();
            template.TemplateName = Path.GetFileNameWithoutExtension(request.TemplateName.Trim());
            template.Content = request.Content ?? string.Empty;
            template.IsPublished = request.IsPublished;
            template.UpdatedAt = DateTime.UtcNow;
            template.UpdatedBy = User.FindFirstValue(ClaimTypes.Name) ?? "admin";

            await _dbContext.SaveChangesAsync();
            return Ok("Prompt template updated.");
        }

        [HttpGet("knowledge")]
        public async Task<IActionResult> GetKnowledge([FromQuery] string profileId = "Documentation", [FromQuery] string? entryType = null)
        {
            var query = _dbContext.AssistantKnowledgeEntries
                .AsNoTracking()
                .Where(x => x.ProfileId == profileId);

            if (!string.IsNullOrWhiteSpace(entryType))
            {
                query = query.Where(x => x.EntryType == entryType);
            }

            var entries = await query
                .OrderBy(x => x.EntryType)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Title)
                .ToListAsync();

            return Ok(entries);
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            var users = await _userManager.Users
                .AsNoTracking()
                .OrderBy(x => x.UserName)
                .ToListAsync();

            var viewModels = new List<BackofficeUserViewModel>(users.Count);
            foreach (var user in users)
            {
                viewModels.Add(await BuildBackofficeUserViewModelAsync(user));
            }

            return Ok(viewModels);
        }

        [HttpPost("users")]
        public async Task<IActionResult> CreateUser([FromBody] CreateBackofficeUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Username, email, and password are required.");
            }

            if (await _userManager.FindByNameAsync(request.Username.Trim()) is not null)
            {
                return BadRequest("Username already exists.");
            }

            if (await _userManager.FindByEmailAsync(request.Email.Trim()) is not null)
            {
                return BadRequest("Email already exists.");
            }

            var user = new ApplicationUser
            {
                UserName = request.Username.Trim(),
                Email = request.Email.Trim(),
                EmailConfirmed = request.IsConfirmed,
                IsConfirmed = request.IsConfirmed,
                IsDisabled = request.IsDisabled
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return BadRequest(string.Join("; ", result.Errors.Select(error => error.Description)));
            }

            var requestedRoles = NormalizeRoles(request.Roles);
            if (requestedRoles.Count == 0)
            {
                requestedRoles = ["AppUser"];
            }

            foreach (var role in requestedRoles)
            {
                await EnsureRoleExistsAsync(role);
            }

            var addRolesResult = await _userManager.AddToRolesAsync(user, requestedRoles);
            if (!addRolesResult.Succeeded)
            {
                return BadRequest(string.Join("; ", addRolesResult.Errors.Select(error => error.Description)));
            }

            return Ok("User created.");
        }

        [HttpPut("users/{id}")]
        public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateBackofficeUserRequest request)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user is null)
            {
                return NotFound("User not found.");
            }

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest("Email is required.");
            }

            var normalizedEmail = request.Email.Trim();
            var existingEmailOwner = await _userManager.FindByEmailAsync(normalizedEmail);
            if (existingEmailOwner is not null && !string.Equals(existingEmailOwner.Id, user.Id, StringComparison.Ordinal))
            {
                return BadRequest("Email already exists.");
            }

            user.Email = normalizedEmail;
            user.EmailConfirmed = request.IsConfirmed;
            user.IsConfirmed = request.IsConfirmed;
            user.IsDisabled = request.IsDisabled;

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return BadRequest(string.Join("; ", updateResult.Errors.Select(error => error.Description)));
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            var targetRoles = NormalizeRoles(request.Roles);
            if (targetRoles.Count == 0)
            {
                targetRoles = ["AppUser"];
            }

            foreach (var role in targetRoles)
            {
                await EnsureRoleExistsAsync(role);
            }

            var rolesToRemove = currentRoles.Except(targetRoles, StringComparer.OrdinalIgnoreCase).ToList();
            if (rolesToRemove.Count > 0)
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
                if (!removeResult.Succeeded)
                {
                    return BadRequest(string.Join("; ", removeResult.Errors.Select(error => error.Description)));
                }
            }

            var rolesToAdd = targetRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();
            if (rolesToAdd.Count > 0)
            {
                var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
                if (!addResult.Succeeded)
                {
                    return BadRequest(string.Join("; ", addResult.Errors.Select(error => error.Description)));
                }
            }

            return Ok("User updated.");
        }

        [HttpPost("knowledge")]
        public async Task<IActionResult> CreateKnowledge([FromBody] SaveKnowledgeEntryRequest request)
        {
            var duplicate = await FindDuplicateKnowledgeEntryAsync(request, null);
            if (duplicate is not null)
            {
                return Conflict($"Knowledge entry already exists: {duplicate.Title}");
            }

            var entry = BuildKnowledgeEntry(request);
            entry.CreatedAt = DateTime.UtcNow;
            entry.UpdatedAt = entry.CreatedAt;
            entry.CreatedBy = User.FindFirstValue(ClaimTypes.Name) ?? "admin";
            entry.UpdatedBy = entry.CreatedBy;

            _dbContext.AssistantKnowledgeEntries.Add(entry);
            await _dbContext.SaveChangesAsync();
            return Ok(new SaveKnowledgeEntryResponse
            {
                Id = entry.Id,
                Message = "Knowledge entry created."
            });
        }

        [HttpPut("knowledge/{id:int}")]
        public async Task<IActionResult> UpdateKnowledge(int id, [FromBody] SaveKnowledgeEntryRequest request)
        {
            var entry = await _dbContext.AssistantKnowledgeEntries.FirstOrDefaultAsync(x => x.Id == id);
            if (entry is null)
            {
                return NotFound("Knowledge entry not found.");
            }

            entry.ProfileId = request.ProfileId.Trim();
            entry.EntryType = request.EntryType.Trim();
            entry.SourceName = request.SourceName.Trim();
            entry.Title = request.Title.Trim();
            entry.Summary = request.Summary?.Trim();
            entry.Content = NormalizeKnowledgeContent(request);
            entry.AliasesJson = SerializeOptionalList(request.Aliases);
            entry.KeywordsJson = SerializeOptionalList(request.Keywords);
            entry.IsPublished = request.IsPublished;
            entry.SortOrder = request.SortOrder;
            entry.UpdatedAt = DateTime.UtcNow;
            entry.UpdatedBy = User.FindFirstValue(ClaimTypes.Name) ?? "admin";

            await _dbContext.SaveChangesAsync();
            return Ok("Knowledge entry updated.");
        }

        [HttpPut("reports/{id:int}/promoted-knowledge/{knowledgeEntryId:int}")]
        public async Task<IActionResult> LinkPromotedKnowledge(int id, int knowledgeEntryId)
        {
            var report = await _dbContext.ChatResponseReports.FirstOrDefaultAsync(x => x.Id == id);
            if (report is null)
            {
                return NotFound("Report not found.");
            }

            var knowledgeExists = await _dbContext.AssistantKnowledgeEntries.AnyAsync(x => x.Id == knowledgeEntryId);
            if (!knowledgeExists)
            {
                return NotFound("Knowledge entry not found.");
            }

            report.PromotedKnowledgeEntryId = knowledgeEntryId;
            report.ReviewedBy = User.FindFirstValue(ClaimTypes.Name) ?? "admin";
            report.ReviewedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            return Ok("Report linked to knowledge entry.");
        }

        private static AssistantKnowledgeEntryEntity BuildKnowledgeEntryFromReview(ChatResponseReportEntity report, ReviewReportedResponseRequest request)
        {
            var entryType = string.IsNullOrWhiteSpace(request.KnowledgeEntryType) ? "QuickAnswer" : request.KnowledgeEntryType.Trim();
            var sourceName = string.IsNullOrWhiteSpace(request.KnowledgeSourceName)
                ? (string.Equals(entryType, "Topic", StringComparison.OrdinalIgnoreCase) ? "Faq" : "QuickAnswers")
                : request.KnowledgeSourceName.Trim();

            var validatedQuestion = request.ValidatedQuestion?.Trim();
            var validatedResponse = request.ValidatedResponse?.Trim();

            return new AssistantKnowledgeEntryEntity
            {
                ProfileId = "Documentation",
                EntryType = entryType,
                SourceName = sourceName,
                Title = string.IsNullOrWhiteSpace(request.KnowledgeTitle) ? validatedQuestion ?? report.UserPrompt : request.KnowledgeTitle.Trim(),
                Summary = request.KnowledgeSummary?.Trim(),
                Content = string.Equals(entryType, "Topic", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(request.ContextLines ?? BuildDefaultContextLines(validatedQuestion, validatedResponse), JsonOptions)
                    : validatedResponse ?? report.AssistantResponse,
                AliasesJson = SerializeOptionalList(request.Aliases ?? BuildDefaultAliases(validatedQuestion, report.UserPrompt, entryType)),
                KeywordsJson = SerializeOptionalList(request.Keywords),
                IsPublished = request.PublishKnowledge,
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = report.ReviewedBy ?? "admin",
                UpdatedBy = report.ReviewedBy ?? "admin"
            };
        }

        private static AssistantKnowledgeEntryEntity BuildKnowledgeEntry(SaveKnowledgeEntryRequest request)
        {
            return new AssistantKnowledgeEntryEntity
            {
                ProfileId = request.ProfileId.Trim(),
                EntryType = request.EntryType.Trim(),
                SourceName = request.SourceName.Trim(),
                Title = request.Title.Trim(),
                Summary = request.Summary?.Trim(),
                Content = NormalizeKnowledgeContent(request),
                AliasesJson = SerializeOptionalList(request.Aliases),
                KeywordsJson = SerializeOptionalList(request.Keywords),
                IsPublished = request.IsPublished,
                SortOrder = request.SortOrder
            };
        }

        private static string? NormalizeKnowledgeContent(SaveKnowledgeEntryRequest request)
        {
            if (string.Equals(request.EntryType, "Topic", StringComparison.OrdinalIgnoreCase))
            {
                var lines = request.Content?
                    .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList() ?? [];
                return JsonSerializer.Serialize(lines, JsonOptions);
            }

            return request.Content?.Trim();
        }

        private static List<string> BuildDefaultAliases(string? validatedQuestion, string originalQuestion, string entryType)
        {
            if (!string.Equals(entryType, "QuickAnswer", StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            return [validatedQuestion ?? originalQuestion];
        }

        private static List<string> BuildDefaultContextLines(string? validatedQuestion, string? validatedResponse)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(validatedQuestion))
            {
                lines.Add(validatedQuestion.Trim());
            }

            if (!string.IsNullOrWhiteSpace(validatedResponse))
            {
                lines.Add(validatedResponse.Trim());
            }

            return lines;
        }

        private static string? SerializeOptionalList(List<string>? values)
        {
            var cleaned = values?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return cleaned is { Count: > 0 }
                ? JsonSerializer.Serialize(cleaned, JsonOptions)
                : null;
        }

        private async Task<AssistantKnowledgeEntryEntity?> FindDuplicateKnowledgeEntryAsync(SaveKnowledgeEntryRequest request, int? excludedEntryId)
        {
            var profileId = string.IsNullOrWhiteSpace(request.ProfileId) ? "Documentation" : request.ProfileId.Trim();
            var entryType = string.IsNullOrWhiteSpace(request.EntryType) ? "Reference" : request.EntryType.Trim();
            var candidateKeys = BuildKnowledgeQuestionKeys(request.Title, request.Aliases);
            if (candidateKeys.Count == 0)
            {
                return null;
            }

            var entries = await _dbContext.AssistantKnowledgeEntries
                .AsNoTracking()
                .Where(x => x.ProfileId == profileId && x.EntryType == entryType)
                .Where(x => !excludedEntryId.HasValue || x.Id != excludedEntryId.Value)
                .ToListAsync();

            return entries.FirstOrDefault(entry =>
            {
                var existingKeys = BuildKnowledgeQuestionKeys(entry.Title, DeserializeOptionalList(entry.AliasesJson));
                return existingKeys.Overlaps(candidateKeys);
            });
        }

        private static HashSet<string> BuildKnowledgeQuestionKeys(string? title, IEnumerable<string>? aliases)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddKnowledgeQuestionKey(keys, title);

            if (aliases is not null)
            {
                foreach (var alias in aliases)
                {
                    AddKnowledgeQuestionKey(keys, alias);
                }
            }

            return keys;
        }

        private static void AddKnowledgeQuestionKey(HashSet<string> keys, string? value)
        {
            var normalized = NormalizeKnowledgeQuestionKey(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                keys.Add(normalized);
            }
        }

        private static string NormalizeKnowledgeQuestionKey(string? value)
        {
            var builder = new StringBuilder();
            foreach (var character in (value ?? string.Empty).ToLowerInvariant())
            {
                builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
            }

            return string.Join(' ', builder
                .ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        private static List<string> DeserializeOptionalList(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? [];
            }
            catch (JsonException)
            {
                return json.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
        }

        private static bool IsTrainingCandidate(ChatResponseReportEntity report)
            => string.Equals(report.ReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(report.ValidatedResponse)
               && !string.IsNullOrWhiteSpace(report.ReviewCategory);

        private async Task<List<ChatResponseReportEntity>> LoadTrainingCandidateEntitiesAsync()
            => await _dbContext.ChatResponseReports
                .AsNoTracking()
                .Where(x => x.ReviewStatus == "Approved"
                    && x.ValidatedResponse != null
                    && x.ValidatedResponse != string.Empty
                    && x.ReviewCategory != null
                    && x.ReviewCategory != string.Empty)
                .OrderByDescending(x => x.ReviewedAt ?? x.CreatedAt)
                .ToListAsync();

        private static TrainingExample ToTrainingExample(ChatResponseReportEntity report)
            => new()
            {
                SourceType = "ReviewedReport",
                SourceReference = $"Report-{report.Id}",
                Question = report.ValidatedQuestion ?? report.UserPrompt,
                BadResponse = report.AssistantResponse,
                ExpectedAnswer = report.ValidatedResponse ?? string.Empty,
                IssueType = report.ReviewCategory ?? "Other",
                Intent = report.ContextMode ?? "DocumentationQuestion",
                ReviewStatus = "Approved",
                ApprovedForTraining = true,
                ReviewedBy = report.ReviewedBy ?? string.Empty,
                ReviewedAt = report.ReviewedAt
            };

        private List<AssistantPromptTemplateEntity> BuildFallbackPromptTemplates(string profileId)
        {
            var templateNames = new[] { "AnswerStyle", "ContinuationTemplate", "RetryTemplate", "SystemContext" };
            var now = DateTime.UtcNow;

            return templateNames.Select((templateName, index) => new AssistantPromptTemplateEntity
            {
                Id = -(index + 1),
                ProfileId = profileId,
                TemplateName = templateName,
                Content = _chatPaths.LoadAssistantPrompt(profileId, $"{templateName}.json"),
                IsPublished = true,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = "fallback",
                UpdatedBy = "fallback"
            }).ToList();
        }

        private async Task<BackofficeUserViewModel> BuildBackofficeUserViewModelAsync(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            return new BackofficeUserViewModel
            {
                Id = user.Id,
                Username = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                IsConfirmed = user.IsConfirmed || user.EmailConfirmed,
                IsDisabled = user.IsDisabled,
                TwoFactorEnabled = user.TwoFactorEnabled,
                Roles = roles.OrderBy(role => role).ToList()
            };
        }

        private async Task EnsureRoleExistsAsync(string role)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                await _roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        private static List<string> NormalizeRoles(IEnumerable<string>? roles)
            => (roles ?? [])
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(role => role.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
