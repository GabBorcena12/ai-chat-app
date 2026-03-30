using Microsoft.AspNetCore.Identity;

namespace AIChatApp.Core.Data_Context.Entity
{
    public class ApplicationUser : IdentityUser
    {
        // Custom fields
        public bool IsConfirmed { get; set; } = false;
        public bool IsDisabled { get; set; } = false;
    }
}
