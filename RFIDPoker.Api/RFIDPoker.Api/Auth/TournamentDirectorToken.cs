namespace RFIDPoker.Api.Auth;

/// <summary>
/// Tournament Director webhook credential. Only the hash of the raw token is
/// persisted; the raw token is shown to the administrator exactly once when
/// generated. Only one token is intended to be active at a time — generating a
/// new token revokes any prior one. TD sends this value in the
/// <c>X-TD-Token</c> HTTP header when calling the webhook.
/// </summary>
public class TournamentDirectorToken
{
    public int Id { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsRevoked { get; set; }
    public string? CreatedByUserId { get; set; }
}
