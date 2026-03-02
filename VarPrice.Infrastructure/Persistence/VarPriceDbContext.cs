using Microsoft.EntityFrameworkCore;

namespace VarPrice.Infrastructure.Persistence;

public sealed class VarPriceDbContext(DbContextOptions<VarPriceDbContext> options) : DbContext(options)
{
    public DbSet<CrawlerRunEntity> CrawlerRuns => Set<CrawlerRunEntity>();

    public DbSet<PriceSnapshotEntity> PriceSnapshots => Set<PriceSnapshotEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CrawlerRunEntity>(entity =>
        {
            entity.ToTable("crawler_run");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("run_id");
            entity.Property(x => x.StartedAtUtc).HasColumnName("started_at");
            entity.Property(x => x.FinishedAtUtc).HasColumnName("finished_at");
            entity.Property(x => x.Status).HasColumnName("status");
        });

        modelBuilder.Entity<PriceSnapshotEntity>(entity =>
        {
            entity.ToTable("price_snapshot");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("snapshot_id");
            entity.Property(x => x.RunId).HasColumnName("run_id");
        });
    }
}

public sealed class CrawlerRunEntity
{
    public long Id { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    public string Status { get; set; } = string.Empty;
}

public sealed class PriceSnapshotEntity
{
    public long Id { get; set; }

    public long RunId { get; set; }
}
