using Microsoft.EntityFrameworkCore;

namespace Analytics.Api.Data;

public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options)
    : DbContext(options);
