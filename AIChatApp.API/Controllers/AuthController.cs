using AIChatApp.API.Model;
using AIChatApp.API.Services.Generic;
using AIChatApp.Core.Data_Context.Entity;
using Inventory.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AichatApp.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly JWTServices _jwtServices;
        private readonly IConfiguration _configuration;
        private readonly EmailService _emailService;
        private readonly GoogleAuthenticatorService _googleAuthenticatorService;
        private static string? _host;
        private const string AuthenticatorIssuer = "AIChatApp";

        public AuthController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            JWTServices jWTServices,
            IConfiguration configuration,
            EmailService emailService,
            GoogleAuthenticatorService googleAuthenticatorService)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _configuration = configuration;
            _jwtServices = jWTServices;
            _roleManager = roleManager;
            _emailService = emailService;
            _googleAuthenticatorService = googleAuthenticatorService;
            _host = configuration.GetValue<string>("AppSettings:BaseUrl") ?? "http://localhost:3000";
        }

        #region JWT
        // GUIDE: 
        // Login flow with optional TOTP-based 2FA:
        // 1. Validate username and password.
        // 2. If 2FA is enabled, require OtpCode from Google Authenticator.
        // 3. Issue the JWT only after the OTP is validated.
        //
        // Initial 2FA setup flow:
        // 1. Log in and get a JWT.
        // 2. Call POST /api/auth/2fa/setup with that JWT.
        // 3. Scan AuthenticatorUri, or enter SharedKey manually, in Google Authenticator.
        // 4. Call POST /api/auth/2fa/verify with the current 6-digit code.
        // 5. Future login requests must include OtpCode.
        //
        // Device replacement flow:
        // 1. Sign in from an existing trusted session.
        // 2. Call POST /api/auth/2fa/setup to generate a new secret.
        // 3. Render the returned otpauth:// URI as a QR code.
        // 4. Scan it on the new device.
        // 5. Submit the new 6-digit code to POST /api/auth/2fa/verify.
        // 6. The new device becomes the active authenticator for future logins.
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginPayload model)
        {
            var user = await _userManager.FindByNameAsync(model.Username);
            if (user == null)
                return Unauthorized("Invalid username or password");
            if (user.IsDisabled)
                return Unauthorized("This account is disabled.");

            var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, false);
            if (!result.Succeeded)
                return Unauthorized("Invalid username or password");

            if (await _userManager.GetTwoFactorEnabledAsync(user))
            {
                if (!_googleAuthenticatorService.ValidateCode(user.TwoFactorSecret, model.OtpCode))
                {
                    return Unauthorized("A valid Google Authenticator code is required.");
                }
            }

            var roles = await _userManager.GetRolesAsync(user);
            var token = _jwtServices.GenerateJwtToken(user, roles);
            return Ok(new { token });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterPayload model)
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(model.Username) ||
                string.IsNullOrWhiteSpace(model.Password) ||
                string.IsNullOrWhiteSpace(model.Email))
            {
                return BadRequest("All fields are required");
            }

            // Check if user already exists
            var existingUser = await _userManager.FindByNameAsync(model.Username);
            if (existingUser != null)
                return BadRequest("Username already exists");

            var existingEmail = await _userManager.FindByEmailAsync(model.Email);
            if (existingEmail != null)
                return BadRequest("Email already exists");

            // Create user
            var user = new ApplicationUser
            {
                UserName = model.Username,
                Email = model.Email
            };

            var roleName = "AppUser";
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                await _roleManager.CreateAsync(new IdentityRole(roleName));
            }

            var result = await _userManager.CreateAsync(user, model.Password);

            if (!result.Succeeded)
            {
                return BadRequest(result.Errors);
            }

            // Assign default role (optional but recommended)
            await _userManager.AddToRoleAsync(user, roleName);
            return Ok("User registered successfully");
        }

        [HttpPost("2fa/setup")]
        [Authorize(AuthenticationSchemes = "LocalJwt")]
        public async Task<IActionResult> SetupTwoFactor()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return Unauthorized();

            var secret = _googleAuthenticatorService.GenerateSecret();
            user.TwoFactorSecret = secret;

            var updateUser = await _userManager.UpdateAsync(user);
            if (!updateUser.Succeeded)
                return BadRequest(updateUser.Errors);

            var accountName = user.Email ?? user.UserName ?? user.Id;
            var authenticatorUri = _googleAuthenticatorService.BuildQrCodeUri(AuthenticatorIssuer, accountName, secret);

            return Ok(new TwoFactorSetupResponse
            {
                SharedKey = secret,
                AuthenticatorUri = authenticatorUri
            });
        }

        [HttpGet("2fa/status")]
        [Authorize(AuthenticationSchemes = "LocalJwt")]
        public async Task<IActionResult> GetTwoFactorStatus()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return Unauthorized();

            var isEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
            return Ok(new TwoFactorStatusResponse
            {
                IsEnabled = isEnabled
            });
        }

        [HttpPost("2fa/verify")]
        [Authorize(AuthenticationSchemes = "LocalJwt")]
        public async Task<IActionResult> VerifyTwoFactor([FromBody] VerifyTwoFactorPayload model)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return Unauthorized();

            if (!_googleAuthenticatorService.ValidateCode(user.TwoFactorSecret, model.Code))
                return BadRequest("Invalid Google Authenticator code.");

            var enableResult = await _userManager.SetTwoFactorEnabledAsync(user, true);
            if (!enableResult.Succeeded)
                return BadRequest(enableResult.Errors);

            return Ok("Google Authenticator is enabled.");
        }

        [HttpPost("2fa/disable")]
        [Authorize(AuthenticationSchemes = "LocalJwt")]
        public async Task<IActionResult> DisableTwoFactor([FromBody] VerifyTwoFactorPayload model)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return Unauthorized();

            if (await _userManager.GetTwoFactorEnabledAsync(user) &&
                !_googleAuthenticatorService.ValidateCode(user.TwoFactorSecret, model.Code))
            {
                return BadRequest("Invalid Google Authenticator code.");
            }

            user.TwoFactorSecret = null;
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
                return BadRequest(updateResult.Errors);

            var disableResult = await _userManager.SetTwoFactorEnabledAsync(user, false);
            if (!disableResult.Succeeded)
                return BadRequest(disableResult.Errors);

            return Ok("Google Authenticator is disabled.");
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordPayload model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return Ok("If the email exists, a reset link has been sent.");

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetLink = $"{_host}/reset-password?email={model.Email}&token={Uri.EscapeDataString(token)}";
            var emailBody = $@"
                <h3>Reset Password</h3>
                <p>Click the link below to reset your password:</p>
                <a href='{resetLink}'>Reset Password</a>
            ";

            await _emailService.SendEmailAsync(model.Email, "Reset Password", emailBody);

            return Ok("Reset link sent to email");
        }

        [HttpPost("test-email-service")]
        [Authorize(AuthenticationSchemes = "LocalJwt")]
        public async Task<IActionResult> TestEmailService()
        {
            var isSuccess = await _emailService.TestAccountAsync();

            if (isSuccess)
                return Ok("✅ Email service is working!");

            return BadRequest("❌ Email service failed. Check SMTP credentials.");
        }
        #endregion

        private async Task<ApplicationUser?> GetCurrentUserAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            return await _userManager.FindByIdAsync(userId);
        }
    }
}
