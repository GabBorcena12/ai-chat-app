namespace AIChatApp.API.Models.Authentication
{
    public class LoginPayload
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public string? OtpCode { get; set; }
    }
}
