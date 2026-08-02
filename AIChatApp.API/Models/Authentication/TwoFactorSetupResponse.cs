namespace AIChatApp.API.Models.Authentication
{
    public class TwoFactorSetupResponse
    {
        public required string SharedKey { get; set; }
        public required string AuthenticatorUri { get; set; }
    }
}
