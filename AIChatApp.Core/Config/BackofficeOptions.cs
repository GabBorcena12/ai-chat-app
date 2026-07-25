namespace AIChatApp.Core.Config;

public sealed class BackofficeOptions
{
    public const string SectionName = "Backoffice";

    public List<string> AdminUsernames { get; set; } = [];
    public bool SeedDefaultAdmin { get; set; } = true;
    public bool SeedDefaultRoleAccounts { get; set; } = true;
    public string DefaultAdminUsername { get; set; } = "admin";
    public string DefaultAdminEmail { get; set; } = "admin@localhost";
    public string DefaultAdminPassword { get; set; } = "Admin123!";
    public string DefaultUserUsername { get; set; } = "user";
    public string DefaultUserEmail { get; set; } = "user@localhost";
    public string DefaultUserPassword { get; set; } = "User123!";
    public string DefaultValidatorUsername { get; set; } = "validator";
    public string DefaultValidatorEmail { get; set; } = "validator@localhost";
    public string DefaultValidatorPassword { get; set; } = "Validator123!";
}
