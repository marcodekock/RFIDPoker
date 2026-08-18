namespace RFIDPoker.Api.Auth;

public static class AuthRoles
{
    public const string Admin = "Admin";
    public const string User = "User";
}

public static class AuthPolicies
{
    public const string RequireAdmin = "RequireAdmin";
    public const string RequireUser = "RequireUser";
    public const string OverlayRead = "OverlayRead";
    /// <summary>Accepts either a user JWT or an overlay JWT. Used for the SignalR hub.</summary>
    public const string UserOrOverlay = "UserOrOverlay";
}

public static class AuthClaims
{
    /// <summary>Distinguishes user JWTs from overlay JWTs so overlay tokens can never satisfy admin/user policies.</summary>
    public const string TokenType = "token_type";
    public const string UserTokenType = "user";
    public const string OverlayTokenType = "overlay";
}
