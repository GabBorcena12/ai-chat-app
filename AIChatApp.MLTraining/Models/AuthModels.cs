namespace AIChatApp.MLTraining.Models;

public sealed class TrainingLoginPayload
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? OtpCode { get; set; }
}

public sealed class TrainingLoginResponse
{
    public string Token { get; set; } = string.Empty;
}
