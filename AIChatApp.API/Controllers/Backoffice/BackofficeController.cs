using AIChatApp.API.Models.Backoffice;
using AIChatApp.API.Services.Backoffice;
using AIChatApp.Core.Config;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AIChatApp.API.Controllers.Backoffice;

/// <summary>
/// Provides the HTTP boundary for report review, managed assistant content, users, and reviewer-model operations.
/// Keep this controller thin: authorization belongs here, while validation and state changes belong in the corresponding Backoffice service.
/// </summary>
[ApiController]
[Route("api/backoffice")]
[Authorize(AuthenticationSchemes = "LocalJwt", Roles = AppRoleNames.BackofficeAccess)]
public sealed class BackofficeController : ControllerBase
{
    private readonly BackofficeReportService _reports;
    private readonly BackofficeContentService _content;
    private readonly BackofficeUserService _users;
    private readonly BackofficeReviewerService _reviewer;

    public BackofficeController(
        BackofficeReportService reports,
        BackofficeContentService content,
        BackofficeUserService users,
        BackofficeReviewerService reviewer)
    {
        _reports = reports;
        _content = content;
        _users = users;
        _reviewer = reviewer;
    }

    [HttpGet("reports")]
    public async Task<IActionResult> GetReports([FromQuery] string? status = null)
        => Ok(await _reports.GetReportsAsync(status));

    [HttpGet("workflow-summary")]
    public async Task<IActionResult> GetWorkflowSummary()
        => Ok(await _reports.GetWorkflowSummaryAsync());

    [HttpGet("training-candidates")]
    [Authorize(AuthenticationSchemes = "LocalJwt", Roles = AppRoleNames.AdminOnly)]
    public async Task<IActionResult> GetTrainingCandidates()
        => Ok(await _reports.GetTrainingCandidatesAsync());

    [HttpGet("reviewer/state")]
    [Authorize(AuthenticationSchemes = "LocalJwt", Roles = AppRoleNames.AdminOnly)]
    public IActionResult GetReviewerState()
        => Ok(_reviewer.GetState());

    [HttpPost("reviewer/build-dataset")]
    [Authorize(AuthenticationSchemes = "LocalJwt", Roles = AppRoleNames.AdminOnly)]
    public async Task<IActionResult> BuildReviewerDataset()
        => Ok(await _reviewer.BuildDatasetAsync());

    [HttpPost("reviewer/train")]
    [Authorize(AuthenticationSchemes = "LocalJwt", Roles = AppRoleNames.AdminOnly)]
    public async Task<IActionResult> TrainReviewer(CancellationToken cancellationToken)
        => ToActionResult(await _reviewer.TrainAsync(CurrentUsername, cancellationToken));

    [HttpPost("reviewer/publish-latest")]
    [Authorize(AuthenticationSchemes = "LocalJwt", Roles = AppRoleNames.AdminOnly)]
    public IActionResult PublishLatestReviewer()
        => ToActionResult(_reviewer.PublishLatest());

    [HttpPut("reports/{id:int}/review")]
    public async Task<IActionResult> ReviewReport(int id, [FromBody] ReviewReportedResponseRequest request)
        => ToActionResult(await _reports.ReviewReportAsync(id, request, CurrentUsername));

    [HttpGet("prompt-templates")]
    public async Task<IActionResult> GetPromptTemplates([FromQuery] string profileId = "Documentation")
        => Ok(await _content.GetPromptTemplatesAsync(profileId));

    [HttpPut("prompt-templates/{id:int}")]
    public async Task<IActionResult> UpdatePromptTemplate(int id, [FromBody] SavePromptTemplateRequest request)
        => ToActionResult(await _content.UpdatePromptTemplateAsync(id, request, CurrentUsername));

    [HttpGet("knowledge")]
    public async Task<IActionResult> GetKnowledge(
        [FromQuery] string profileId = "Documentation",
        [FromQuery] string? entryType = null)
        => Ok(await _content.GetKnowledgeAsync(profileId, entryType));

    [HttpGet("users")]
    [Authorize(AuthenticationSchemes = "LocalJwt", Roles = AppRoleNames.AdminOnly)]
    public async Task<IActionResult> GetUsers()
        => Ok(await _users.GetUsersAsync());

    [HttpPost("users")]
    [Authorize(AuthenticationSchemes = "LocalJwt", Roles = AppRoleNames.AdminOnly)]
    public async Task<IActionResult> CreateUser([FromBody] CreateBackofficeUserRequest request)
        => ToActionResult(await _users.CreateUserAsync(request));

    [HttpPut("users/{id}")]
    [Authorize(AuthenticationSchemes = "LocalJwt", Roles = AppRoleNames.AdminOnly)]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateBackofficeUserRequest request)
        => ToActionResult(await _users.UpdateUserAsync(id, request));

    [HttpPost("knowledge")]
    public async Task<IActionResult> CreateKnowledge([FromBody] SaveKnowledgeEntryRequest request)
        => ToActionResult(await _content.CreateKnowledgeAsync(request, CurrentUsername));

    [HttpPut("knowledge/{id:int}")]
    public async Task<IActionResult> UpdateKnowledge(int id, [FromBody] SaveKnowledgeEntryRequest request)
        => ToActionResult(await _content.UpdateKnowledgeAsync(id, request, CurrentUsername));

    [HttpPut("reports/{id:int}/promoted-knowledge/{knowledgeEntryId:int}")]
    public async Task<IActionResult> LinkPromotedKnowledge(int id, int knowledgeEntryId)
        => ToActionResult(await _reports.LinkPromotedKnowledgeAsync(id, knowledgeEntryId, CurrentUsername));

    private string CurrentUsername => User.FindFirstValue(ClaimTypes.Name) ?? "admin";

    private IActionResult ToActionResult(BackofficeResult result)
        => result.Status switch
        {
            BackofficeResultStatus.Success => Ok(result.Value),
            BackofficeResultStatus.BadRequest => BadRequest(result.Value),
            BackofficeResultStatus.NotFound => NotFound(result.Value),
            BackofficeResultStatus.Conflict => Conflict(result.Value),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
}
