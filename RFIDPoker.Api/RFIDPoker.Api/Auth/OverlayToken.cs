namespace RFIDPoker.Api.Auth;

/// <summary>
/// Single-installation OBS overlay authentication credential. Only the hash of the raw
/// token is persisted; the raw token is shown to the administrator exactly once when
/// generated. Only one token is intended to be active at a time — generating a new
/// token revokes any prior one.
/// </summary>
public class OverlayToken
{
    public int Id { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public string? CreatedByUserId { get; set; }
}
