using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RFIDPoker.Api.Data;

namespace RFIDPoker.Api.Auth;

public record TournamentDirectorTokenStatus(
    bool HasActiveToken,
    DateTimeOffset? CreatedAt,
    bool IsRevoked);

public record TournamentDirectorTokenGenerated(string RawToken);

public interface ITournamentDirectorTokenService
{
    Task<TournamentDirectorTokenStatus> GetStatusAsync(CancellationToken ct = default);
    Task<TournamentDirectorTokenGenerated> GenerateAsync(string? createdByUserId, CancellationToken ct = default);
    Task RevokeAsync(CancellationToken ct = default);

    /// <summary>Returns true if the supplied raw token matches a non-revoked stored token.</summary>
    Task<bool> ValidateAsync(string rawToken, CancellationToken ct = default);
}

public class TournamentDirectorTokenService(AppDbContext db) : ITournamentDirectorTokenService
{
    public async Task<TournamentDirectorTokenStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var latest = await db.Set<TournamentDirectorToken>()
            .OrderByDescending(t => t.Id)
            .FirstOrDefaultAsync(ct);
        if (latest is null)
            return new TournamentDirectorTokenStatus(false, null, false);
        return new TournamentDirectorTokenStatus(!latest.IsRevoked, latest.CreatedAt, latest.IsRevoked);
    }

    public async Task<TournamentDirectorTokenGenerated> GenerateAsync(string? createdByUserId, CancellationToken ct = default)
    {
        // One active token at a time — revoke prior tokens.
        var existing = await db.Set<TournamentDirectorToken>().Where(t => !t.IsRevoked).ToListAsync(ct);
        foreach (var e in existing) e.IsRevoked = true;

        var raw = RandomNumberGenerator.GetBytes(48);
        var rawStr = Convert.ToBase64String(raw)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var hash = Hash(rawStr);

        db.Set<TournamentDirectorToken>().Add(new TournamentDirectorToken
        {
            TokenHash = hash,
            CreatedAt = DateTimeOffset.UtcNow,
            IsRevoked = false,
            CreatedByUserId = createdByUserId
        });
        await db.SaveChangesAsync(ct);
        return new TournamentDirectorTokenGenerated(rawStr);
    }

    public async Task RevokeAsync(CancellationToken ct = default)
    {
        var existing = await db.Set<TournamentDirectorToken>().Where(t => !t.IsRevoked).ToListAsync(ct);
        foreach (var e in existing) e.IsRevoked = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> ValidateAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return false;
        var hash = Hash(rawToken);
        return await db.Set<TournamentDirectorToken>()
            .AsNoTracking()
            .AnyAsync(t => !t.IsRevoked && t.TokenHash == hash, ct);
    }

    private static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
