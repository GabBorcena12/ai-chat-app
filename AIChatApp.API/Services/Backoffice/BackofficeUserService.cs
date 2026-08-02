using AIChatApp.API.Models.Backoffice;
using AIChatApp.Core.Config;
using AIChatApp.Core.Data_Context.Entity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AIChatApp.API.Services.Backoffice;

/// <summary>
/// Manages Identity users, account state, profile fields, and role assignments for Backoffice administrators.
/// Use UserManager and RoleManager APIs so password and security-stamp behavior remains consistent with ASP.NET Core Identity.
/// </summary>
public sealed class BackofficeUserService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public BackofficeUserService(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<IReadOnlyList<BackofficeUserViewModel>> GetUsersAsync()
    {
        var users = await _userManager.Users.AsNoTracking().OrderBy(x => x.UserName).ToListAsync();
        var viewModels = new List<BackofficeUserViewModel>(users.Count);
        foreach (var user in users)
        {
            viewModels.Add(await BuildViewModelAsync(user));
        }

        return viewModels;
    }

    public async Task<BackofficeResult> CreateUserAsync(CreateBackofficeUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BackofficeResult.BadRequest("Username, email, and password are required.");
        }

        if (await _userManager.FindByNameAsync(request.Username.Trim()) is not null)
        {
            return BackofficeResult.BadRequest("Username already exists.");
        }

        if (await _userManager.FindByEmailAsync(request.Email.Trim()) is not null)
        {
            return BackofficeResult.BadRequest("Email already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = request.Username.Trim(),
            Email = request.Email.Trim(),
            EmailConfirmed = request.IsConfirmed,
            IsConfirmed = request.IsConfirmed,
            IsDisabled = request.IsDisabled
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BackofficeResult.BadRequest(JoinErrors(result));
        }

        var requestedRoles = NormalizeRoles(request.Roles);
        if (requestedRoles.Count == 0)
        {
            requestedRoles = [AppRoleNames.User];
        }

        foreach (var role in requestedRoles)
        {
            await EnsureRoleExistsAsync(role);
        }

        var addRolesResult = await _userManager.AddToRolesAsync(user, requestedRoles);
        return addRolesResult.Succeeded
            ? BackofficeResult.Ok("User created.")
            : BackofficeResult.BadRequest(JoinErrors(addRolesResult));
    }

    public async Task<BackofficeResult> UpdateUserAsync(string id, UpdateBackofficeUserRequest request)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return BackofficeResult.NotFound("User not found.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BackofficeResult.BadRequest("Email is required.");
        }

        var normalizedEmail = request.Email.Trim();
        var existingEmailOwner = await _userManager.FindByEmailAsync(normalizedEmail);
        if (existingEmailOwner is not null && !string.Equals(existingEmailOwner.Id, user.Id, StringComparison.Ordinal))
        {
            return BackofficeResult.BadRequest("Email already exists.");
        }

        user.Email = normalizedEmail;
        user.EmailConfirmed = request.IsConfirmed;
        user.IsConfirmed = request.IsConfirmed;
        user.IsDisabled = request.IsDisabled;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return BackofficeResult.BadRequest(JoinErrors(updateResult));
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        var targetRoles = NormalizeRoles(request.Roles);
        if (targetRoles.Count == 0)
        {
            targetRoles = [AppRoleNames.User];
        }

        foreach (var role in targetRoles)
        {
            await EnsureRoleExistsAsync(role);
        }

        var rolesToRemove = currentRoles.Except(targetRoles, StringComparer.OrdinalIgnoreCase).ToList();
        if (rolesToRemove.Count > 0)
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                return BackofficeResult.BadRequest(JoinErrors(removeResult));
            }
        }

        var rolesToAdd = targetRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();
        if (rolesToAdd.Count > 0)
        {
            var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                return BackofficeResult.BadRequest(JoinErrors(addResult));
            }
        }

        return BackofficeResult.Ok("User updated.");
    }

    private async Task<BackofficeUserViewModel> BuildViewModelAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return new BackofficeUserViewModel
        {
            Id = user.Id,
            Username = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            IsConfirmed = user.IsConfirmed || user.EmailConfirmed,
            IsDisabled = user.IsDisabled,
            TwoFactorEnabled = user.TwoFactorEnabled,
            Roles = roles.Select(NormalizeRoleName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(role => role).ToList()
        };
    }

    private async Task EnsureRoleExistsAsync(string role)
    {
        if (!await _roleManager.RoleExistsAsync(role))
        {
            await _roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    private static List<string> NormalizeRoles(IEnumerable<string>? roles)
        => (roles ?? [])
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => NormalizeRoleName(role.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string NormalizeRoleName(string role)
        => role.Equals(AppRoleNames.LegacyAppUser, StringComparison.OrdinalIgnoreCase)
            ? AppRoleNames.User
            : role.Equals(AppRoleNames.LegacyDataValidator, StringComparison.OrdinalIgnoreCase)
                ? AppRoleNames.Validator
                : role;

    private static string JoinErrors(IdentityResult result)
        => string.Join("; ", result.Errors.Select(error => error.Description));
}
