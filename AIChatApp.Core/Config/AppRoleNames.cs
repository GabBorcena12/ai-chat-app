namespace AIChatApp.Core.Config;

public static class AppRoleNames
{
    public const string Admin = "Admin";
    public const string User = "User";
    public const string Validator = "Validator";

    public const string LegacyAppUser = "AppUser";
    public const string LegacyDataValidator = "DataValidator";

    public const string BackofficeAccess = $"{Admin},{Validator},{LegacyDataValidator}";
    public const string AdminOnly = Admin;
}
