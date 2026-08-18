using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using RFIDPoker.Api.Data;

namespace RFIDPoker.Api.Auth;

public interface IJwtTokenService
{
    Task<string> CreateUserTokenAsync(ApplicationUser user);
    string CreateOverlayToken(int overlayTokenId, DateTimeOffset expiresAt);
}

public class JwtTokenService(JwtOptions options, UserManager<ApplicationUser> users) : IJwtTokenService
{
    public async Task<string> CreateUserTokenAsync(ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(AuthClaims.TokenType, AuthClaims.UserTokenType),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var creds = new SigningCredentials(options.Key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateOverlayToken(int overlayTokenId, DateTimeOffset expiresAt)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, $"overlay:{overlayTokenId}"),
            new Claim(AuthClaims.TokenType, AuthClaims.OverlayTokenType),
            new Claim("overlay_id", overlayTokenId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var creds = new SigningCredentials(options.Key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
