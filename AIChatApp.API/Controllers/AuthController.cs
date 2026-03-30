using AIChatApp.API.Model;
using AIChatApp.API.Services.Generic;
using AIChatApp.Core.Data_Context.Entity;
using Inventory.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;

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
        private static string _host;

        public AuthController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            JWTServices jWTServices,
            IConfiguration configuration,
            EmailService emailService)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _configuration = configuration;
            _jwtServices = jWTServices;
            _roleManager = roleManager;
            _emailService = emailService;
            _host = configuration.GetValue<string>("AppSettings:BaseUrl") ?? "http://localhost:3000";
        }

        #region JWT
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginPayload model)
        {
            var user = await _userManager.FindByNameAsync(model.Username);
            if (user == null)
                return Unauthorized("Invalid username or password");

            var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, false);
            if (!result.Succeeded)
                return Unauthorized("Invalid username or password");

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

            var roleName = "User";
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

        [HttpPost("reset-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest model)
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
    }
}
