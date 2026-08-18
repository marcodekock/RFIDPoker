using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Data;

namespace RFIDPoker.Api.Controllers;

public record UserDto(string Id, string Username, IList<string> Roles, bool IsActive, DateTimeOffset CreatedAt);
public record CreateUserRequest(string Username, string Password, string Role);
public record UpdateUserRequest(string? Role, bool? IsActive);
public record ResetPasswordRequest(string NewPassword);

[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthPolicies.RequireAdmin)]
public class UsersController(UserManager<ApplicationUser> users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> List()
    {
        var list = await users.Users.OrderBy(u => u.UserName).ToListAsync();
        var result = new List<UserDto>(list.Count);
        foreach (var u in list)
        {
            var roles = await users.GetRolesAsync(u);
            result.Add(new UserDto(u.Id, u.UserName!, roles, u.IsActive, u.CreatedAt));
        }
        return result;
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "Username and password are required." });
        var role = NormalizeRole(req.Role);
        if (role is null) return BadRequest(new { message = "Invalid role." });

        var user = new ApplicationUser { UserName = req.Username, IsActive = true };
        var create = await users.CreateAsync(user, req.Password);
        if (!create.Succeeded)
            return BadRequest(new { message = string.Join("; ", create.Errors.Select(e => e.Description)) });

        await users.AddToRoleAsync(user, AuthRoles.User);
        if (role == AuthRoles.Admin) await users.AddToRoleAsync(user, AuthRoles.Admin);

        var roles = await users.GetRolesAsync(user);
        return new UserDto(user.Id, user.UserName!, roles, user.IsActive, user.CreatedAt);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserRequest req)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();

        if (req.IsActive is bool active)
        {
            if (!active && await LastActiveAdminAsync(user))
                return BadRequest(new { message = "Cannot deactivate the last active administrator." });
            user.IsActive = active;
        }

        if (req.Role is not null)
        {
            var role = NormalizeRole(req.Role);
            if (role is null) return BadRequest(new { message = "Invalid role." });

            var current = await users.GetRolesAsync(user);
            if (role == AuthRoles.Admin && !current.Contains(AuthRoles.Admin))
                await users.AddToRoleAsync(user, AuthRoles.Admin);
            else if (role == AuthRoles.User && current.Contains(AuthRoles.Admin))
            {
                if (await LastActiveAdminAsync(user))
                    return BadRequest(new { message = "Cannot demote the last active administrator." });
                await users.RemoveFromRoleAsync(user, AuthRoles.Admin);
            }
        }

        await users.UpdateAsync(user);
        return NoContent();
    }

    [HttpPost("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] ResetPasswordRequest req)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, req.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { message = string.Join("; ", result.Errors.Select(e => e.Description)) });
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (await LastActiveAdminAsync(user))
            return BadRequest(new { message = "Cannot delete the last active administrator." });
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded)
            return BadRequest(new { message = string.Join("; ", result.Errors.Select(e => e.Description)) });
        return NoContent();
    }

    private static string? NormalizeRole(string role) => role switch
    {
        AuthRoles.Admin => AuthRoles.Admin,
        AuthRoles.User => AuthRoles.User,
        _ => null
    };

    private async Task<bool> LastActiveAdminAsync(ApplicationUser candidate)
    {
        var isAdmin = await users.IsInRoleAsync(candidate, AuthRoles.Admin);
        if (!isAdmin) return false;
        var admins = await users.GetUsersInRoleAsync(AuthRoles.Admin);
        return admins.Count(a => a.IsActive) <= 1;
    }
}
