using Microsoft.AspNetCore.Identity.Data;

namespace AIChatApp.API.Model
{
    public class RegisterPayload
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public required string Email { get; set; }
    }
}
