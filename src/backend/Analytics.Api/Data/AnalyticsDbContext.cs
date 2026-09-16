using Analytics.Api.Identity;
using Analytics.Api.InstagramAccounts.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Api.Data;

public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<InstagramAccount> InstagramAccounts => Set<InstagramAccount>();

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
    }
}
