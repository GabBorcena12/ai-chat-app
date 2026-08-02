namespace AIChatApp.API.Services.Backoffice;

/// <summary>
/// Describes service outcomes that the Backoffice controller maps to HTTP status codes.
/// </summary>
public enum BackofficeResultStatus
{
    Success,
    BadRequest,
    NotFound,
    Conflict
}

/// <summary>
/// Keeps Backoffice services independent from MVC result types while preserving a consistent controller mapping.
/// </summary>
public sealed record BackofficeResult(BackofficeResultStatus Status, object? Value)
{
    public static BackofficeResult Ok(object? value) => new(BackofficeResultStatus.Success, value);
    public static BackofficeResult BadRequest(string message) => new(BackofficeResultStatus.BadRequest, message);
    public static BackofficeResult NotFound(string message) => new(BackofficeResultStatus.NotFound, message);
    public static BackofficeResult Conflict(string message) => new(BackofficeResultStatus.Conflict, message);
}
