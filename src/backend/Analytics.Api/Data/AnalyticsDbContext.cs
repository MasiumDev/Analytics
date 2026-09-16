using Analytics.Api.Identity;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramCredentials.Domain;
using Analytics.Api.InstagramIntegration.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Api.Data;

public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<InstagramAccount> InstagramAccounts => Set<InstagramAccount>();

    public DbSet<InstagramCredential> InstagramCredentials => Set<InstagramCredential>();

    public DbSet<InstagramOAuthState> InstagramOAuthStates => Set<InstagramOAuthState>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<InstagramAccount>(account =>
        {
            account.ToTable("InstagramAccounts");
            account.HasKey(item => item.Id);
            account.Property(item => item.InstagramUserId).HasMaxLength(64).IsRequired();
            account.Property(item => item.Username).HasMaxLength(64).IsRequired();
            account.Property(item => item.DisplayName).HasMaxLength(200);
            account.HasIndex(item => item.InstagramUserId).IsUnique();
            account.HasIndex(item => new { item.OwnerUserId, item.Username });
            account.HasOne(item => item.Owner)
                .WithMany(user => user.InstagramAccounts)
                .HasForeignKey(item => item.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<InstagramCredential>(credential =>
        {
            credential.ToTable("InstagramCredentials");
            credential.HasKey(item => item.Id);
            credential.Property(item => item.EncryptedAccessToken).IsRequired();
            credential.Property(item => item.GrantedScopes).HasMaxLength(2000).IsRequired();
            credential.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            credential.Property(item => item.RowVersion).IsRowVersion();
            credential.HasIndex(item => item.InstagramAccountId).IsUnique();
            credential.HasOne(item => item.InstagramAccount)
                .WithOne(account => account.Credential)
                .HasForeignKey<InstagramCredential>(item => item.InstagramAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<InstagramOAuthState>(oauthState =>
        {
            oauthState.ToTable("InstagramOAuthStates");
            oauthState.HasKey(item => item.Id);
            oauthState.Property(item => item.StateHash).HasMaxLength(64).IsRequired();
            oauthState.Property(item => item.RowVersion).IsRowVersion();
            oauthState.HasIndex(item => item.StateHash).IsUnique();
            oauthState.HasIndex(item => new { item.OwnerUserId, item.ExpiresAtUtc });
            oauthState.HasOne(item => item.Owner)
                .WithMany()
                .HasForeignKey(item => item.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
