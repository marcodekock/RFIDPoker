using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Data;

namespace RFIDPoker.Api.Controllers;

public record SetupStatusDto(bool NeedsSetup);
public record InitialSetupRequest(string Username, string Password);
public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, string Username, IList<string> Roles);
public record CurrentUserDto(string Username, IList<string> Roles);

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IJwtTokenService jwt) : ControllerBase
{
    /// <summary>Returns whether the installation has zero users — i.e. first-run setup is required.</summary>
    [HttpGet("setup-status")]
    [AllowAnonymous]
    public async Task<ActionResult<SetupStatusDto>> SetupStatus()
    {
        var any = users.Users.Any();
        return new SetupStatusDto(!any);
    }

    /// <summary>First-run administrator creation. Available only while no users exist.</summary>
    [HttpPost("setup")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Setup([FromBody] InitialSetupRequest req)
    {
        if (users.Users.Any())
            return Conflict(new { message = "Initial setup has already been completed." });
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "Username and password are required." });

        var user = new ApplicationUser { UserName = req.Username, IsActive = true };
        var create = await users.CreateAsync(user, req.Password);
        if (!create.Succeeded)
            return BadRequest(new { message = string.Join("; ", create.Errors.Select(e => e.Description)) });

        await users.AddToRoleAsync(user, AuthRoles.Admin);
        await users.AddToRoleAsync(user, AuthRoles.User);

        var token = await jwt.CreateUserTokenAsync(user);
        var roles = await users.GetRolesAsync(user);
        return new LoginResponse(token, user.UserName!, roles);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req)
    {
        var user = await users.FindByNameAsync(req.Username);
        if (user is null || !user.IsActive)
            return Unauthorized(new { message = "Invalid credentials." });

        var result = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: false);
        if (!result.Succeeded)
            return Unauthorized(new { message = "Invalid credentials." });

        var token = await jwt.CreateUserTokenAsync(user);
        var roles = await users.GetRolesAsync(user);
        return new LoginResponse(token, user.UserName!, roles);
    }

    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.RequireUser)]
    public async Task<ActionResult<CurrentUserDto>> Me()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var roles = await users.GetRolesAsync(user);
        return new CurrentUserDto(user.UserName!, roles);
    }
}
