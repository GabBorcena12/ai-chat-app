namespace AIChatApp.API.Models.Authentication
{
    public class VerifyTwoFactorPayload
    {
        public required string Code { get; set; }
    }
}
