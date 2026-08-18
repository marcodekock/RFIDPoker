using Microsoft.AspNetCore.Identity;

namespace RFIDPoker.Api.Auth;

public class ApplicationUser : IdentityUser
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;
}
