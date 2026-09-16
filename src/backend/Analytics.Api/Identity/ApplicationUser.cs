using Analytics.Api.InstagramAccounts.Domain;
using Microsoft.AspNetCore.Identity;

namespace Analytics.Api.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public ICollection<InstagramAccount> InstagramAccounts { get; } =
        new List<InstagramAccount>();
}
