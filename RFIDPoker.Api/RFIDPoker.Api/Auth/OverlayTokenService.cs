using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RFIDPoker.Api.Data;

namespace RFIDPoker.Api.Auth;

public record OverlayTokenStatus(
    bool HasActiveToken,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt,
    bool IsRevoked,
    bool IsExpired);

public record OverlayTokenGenerated(string RawToken, DateTimeOffset ExpiresAt);

public interface IOverlayTokenService
{
    Task<OverlayTokenStatus> GetStatusAsync(CancellationToken ct = default);
    Task<OverlayTokenGenerated> GenerateAsync(TimeSpan lifetime, string? createdByUserId, CancellationToken ct = default);
    Task RevokeAsync(CancellationToken ct = default);
}

public class OverlayTokenService(AppDbContext db, IJwtTokenService jwt) : IOverlayTokenService
{
    public async Task<OverlayTokenStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var latest = await db.OverlayTokens
            .OrderByDescending(t => t.Id)
            .FirstOrDefaultAsync(ct);
        if (latest is null)
            return new OverlayTokenStatus(false, null, null, false, false);

        var expired = latest.ExpiresAt <= DateTimeOffset.UtcNow;
        var hasActive = !latest.IsRevoked && !expired;
        return new OverlayTokenStatus(hasActive, latest.CreatedAt, latest.ExpiresAt, latest.IsRevoked, expired);
    }

    public async Task<OverlayTokenGenerated> GenerateAsync(TimeSpan lifetime, string? createdByUserId, CancellationToken ct = default)
    {
        // Revoke every prior token — only one active token is intended.
        var existing = await db.OverlayTokens.Where(t => !t.IsRevoked).ToListAsync(ct);
        foreach (var e in existing) e.IsRevoked = true;

        // 48-byte cryptographically-strong random token, URL-safe.
        var raw = RandomNumberGenerator.GetBytes(48);
        var rawStr = Convert.ToBase64String(raw)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var hash = Hash(rawStr);

        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        var row = new OverlayToken
        {
            TokenHash = hash,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
            IsRevoked = false,
            CreatedByUserId = createdByUserId
        };
        db.OverlayTokens.Add(row);
        await db.SaveChangesAsync(ct);

        // Combine the DB id (so revocation checks work) with a signed JWT so we
        // don't have to hit the DB on every hub tick.
        var jwtToken = jwt.CreateOverlayToken(row.Id, expiresAt);
        return new OverlayTokenGenerated(jwtToken, expiresAt);
    }

    public async Task RevokeAsync(CancellationToken ct = default)
    {
        var existing = await db.OverlayTokens.Where(t => !t.IsRevoked).ToListAsync(ct);
        foreach (var e in existing) e.IsRevoked = true;
        await db.SaveChangesAsync(ct);
    }

    private static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
