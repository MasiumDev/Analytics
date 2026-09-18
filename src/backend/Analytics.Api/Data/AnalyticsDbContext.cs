using Analytics.Api.Identity;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.InstagramCredentials.Domain;
using Analytics.Api.InstagramIntegration.Domain;
using Analytics.Api.InstagramMedia.Domain;
using InstagramMediaEntity = Analytics.Api.InstagramMedia.Domain.InstagramMedia;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Analytics.Api.Data;

public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<InstagramAccount> InstagramAccounts => Set<InstagramAccount>();

    public DbSet<InstagramCredential> InstagramCredentials => Set<InstagramCredential>();

    public DbSet<InstagramOAuthState> InstagramOAuthStates => Set<InstagramOAuthState>();

    public DbSet<InstagramMediaEntity> InstagramMedia => Set<InstagramMediaEntity>();

    public DbSet<MediaCurrentStats> MediaCurrentStats => Set<MediaCurrentStats>();

    public DbSet<AccountCurrentStats> AccountCurrentStats => Set<AccountCurrentStats>();

    public DbSet<MediaInsightSnapshot> MediaInsightSnapshots =>
        Set<MediaInsightSnapshot>();

    public DbSet<AccountInsightSnapshot> AccountInsightSnapshots =>
        Set<AccountInsightSnapshot>();

    public DbSet<MediaImportCheckpoint> MediaImportCheckpoints =>
        Set<MediaImportCheckpoint>();

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
            account.Property(item => item.ProfessionalAccountType)
                .HasConversion<string>()
                .HasMaxLength(32);
            account.Property(item => item.ConnectionStatus)
                .HasConversion<string>()
                .HasMaxLength(32);
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
            credential.Property(item => item.EncryptedAccessToken);
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

        builder.Entity<InstagramMediaEntity>(media =>
        {
            media.ToTable("InstagramMedia");
            media.HasKey(item => item.Id);
            media.Property(item => item.InstagramMediaId).HasMaxLength(64).IsRequired();
            media.Property(item => item.MediaType)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            media.Property(item => item.Permalink).HasMaxLength(2048).IsRequired();
            media.Property(item => item.Caption).HasMaxLength(2200);
            media.Property(item => item.RowVersion).IsRowVersion();
            media.HasIndex(item => new { item.InstagramAccountId, item.InstagramMediaId })
                .IsUnique();
            media.HasIndex(item => new { item.InstagramAccountId, item.PublishedAtUtc })
                .IsDescending(false, true);
            media.HasOne(item => item.InstagramAccount)
                .WithMany()
                .HasForeignKey(item => item.InstagramAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MediaCurrentStats>(stats =>
        {
            stats.ToTable("MediaCurrentStats");
            stats.HasKey(item => item.InstagramMediaId);
            stats.Property(item => item.RowVersion).IsRowVersion();
            stats.HasOne(item => item.InstagramMedia)
                .WithOne(media => media.CurrentStats)
                .HasForeignKey<MediaCurrentStats>(item => item.InstagramMediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AccountCurrentStats>(stats =>
        {
            stats.ToTable("AccountCurrentStats");
            stats.HasKey(item => item.InstagramAccountId);
            stats.Property(item => item.RowVersion).IsRowVersion();
            stats.HasOne(item => item.InstagramAccount)
                .WithOne()
                .HasForeignKey<AccountCurrentStats>(item => item.InstagramAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MediaInsightSnapshot>(snapshot =>
        {
            snapshot.ToTable(
                "MediaInsightSnapshots",
                table => table.HasCheckConstraint(
                    "CK_MediaInsightSnapshots_NonNegativeMetrics",
                    "([ViewsCount] IS NULL OR [ViewsCount] >= 0) AND " +
                    "([ReachCount] IS NULL OR [ReachCount] >= 0) AND " +
                    "([LikesCount] IS NULL OR [LikesCount] >= 0) AND " +
                    "([CommentsCount] IS NULL OR [CommentsCount] >= 0) AND " +
                    "([SavesCount] IS NULL OR [SavesCount] >= 0) AND " +
                    "([SharesCount] IS NULL OR [SharesCount] >= 0) AND " +
                    "([TotalInteractionsCount] IS NULL OR " +
                    "[TotalInteractionsCount] >= 0) AND " +
                    "([AverageWatchTimeMilliseconds] IS NULL OR " +
                    "[AverageWatchTimeMilliseconds] >= 0) AND " +
                    "([TotalWatchTimeMilliseconds] IS NULL OR " +
                    "[TotalWatchTimeMilliseconds] >= 0)"));
            snapshot.HasKey(item => item.Id);
            snapshot.HasIndex(item => new { item.InstagramMediaId, item.CapturedAtUtc })
                .IsUnique()
                .IsDescending(false, true);
            snapshot.HasIndex(item => item.CapturedAtUtc);
            snapshot.HasOne(item => item.InstagramMedia)
                .WithMany()
                .HasForeignKey(item => item.InstagramMediaId)
                .OnDelete(DeleteBehavior.Cascade);
            ConfigureAppendOnly(snapshot.Metadata);
        });

        builder.Entity<AccountInsightSnapshot>(snapshot =>
        {
            snapshot.ToTable(
                "AccountInsightSnapshots",
                table => table.HasCheckConstraint(
                    "CK_AccountInsightSnapshots_NonNegativeMetrics",
                    "([ViewsCount] IS NULL OR [ViewsCount] >= 0) AND " +
                    "([ReachCount] IS NULL OR [ReachCount] >= 0) AND " +
                    "([FollowerCount] IS NULL OR [FollowerCount] >= 0) AND " +
                    "([ProfileViewsCount] IS NULL OR [ProfileViewsCount] >= 0) AND " +
                    "([WebsiteClicksCount] IS NULL OR [WebsiteClicksCount] >= 0) AND " +
                    "([AccountsEngagedCount] IS NULL OR " +
                    "[AccountsEngagedCount] >= 0) AND " +
                    "([TotalInteractionsCount] IS NULL OR " +
                    "[TotalInteractionsCount] >= 0)"));
            snapshot.HasKey(item => item.Id);
            snapshot.HasIndex(item => new { item.InstagramAccountId, item.CapturedAtUtc })
                .IsUnique()
                .IsDescending(false, true);
            snapshot.HasIndex(item => item.CapturedAtUtc);
            snapshot.HasOne(item => item.InstagramAccount)
                .WithMany()
                .HasForeignKey(item => item.InstagramAccountId)
                .OnDelete(DeleteBehavior.Cascade);
            ConfigureAppendOnly(snapshot.Metadata);
        });

        builder.Entity<MediaImportCheckpoint>(checkpoint =>
        {
            checkpoint.ToTable("MediaImportCheckpoints");
            checkpoint.HasKey(item => item.InstagramAccountId);
            checkpoint.Property(item => item.AfterCursor).HasMaxLength(1024);
            checkpoint.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            checkpoint.Property(item => item.RowVersion).IsRowVersion();
            checkpoint.HasOne(item => item.InstagramAccount)
                .WithOne()
                .HasForeignKey<MediaImportCheckpoint>(item => item.InstagramAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureAppendOnly(IMutableEntityType entityType)
    {
        foreach (var property in entityType.GetProperties())
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
    }
}
