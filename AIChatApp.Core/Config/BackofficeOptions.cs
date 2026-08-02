namespace AIChatApp.Core.Config;

public sealed class BackofficeOptions
{
    public const string SectionName = "Backoffice";

    public List<string> AdminUsernames { get; set; } = [];
    public bool SeedDefaultAdmin { get; set; }
    public bool SeedDefaultRoleAccounts { get; set; }
    public string DefaultAdminUsername { get; set; } = string.Empty;
    public string DefaultAdminEmail { get; set; } = string.Empty;
    public string DefaultAdminPassword { get; set; } = string.Empty;
    public string DefaultUserUsername { get; set; } = string.Empty;
    public string DefaultUserEmail { get; set; } = string.Empty;
    public string DefaultUserPassword { get; set; } = string.Empty;
    public string DefaultValidatorUsername { get; set; } = string.Empty;
    public string DefaultValidatorEmail { get; set; } = string.Empty;
    public string DefaultValidatorPassword { get; set; } = string.Empty;
}
