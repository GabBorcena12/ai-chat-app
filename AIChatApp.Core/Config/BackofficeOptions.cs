namespace AIChatApp.Core.Config;

public sealed class BackofficeOptions
{
    public const string SectionName = "Backoffice";

    public List<string> AdminUsernames { get; set; } = [];
    public bool SeedDefaultAdmin { get; set; } = true;
    public string DefaultAdminUsername { get; set; } = "admin";
    public string DefaultAdminEmail { get; set; } = "admin@localhost";
    public string DefaultAdminPassword { get; set; } = "Admin123!";
}
