using Microsoft.EntityFrameworkCore;

namespace VarPrice.Infrastructure.Persistence;

public sealed class VarPriceDbContext(DbContextOptions<VarPriceDbContext> options) : DbContext(options)
{
    public DbSet<CrawlerRunEntity> CrawlerRuns => Set<CrawlerRunEntity>();

    public DbSet<IngestionRunEntity> IngestionRuns => Set<IngestionRunEntity>();

    public DbSet<ProductEntity> Products => Set<ProductEntity>();

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
            entity.Property(x => x.CapturedAtUtc).HasColumnName("captured_at");
            entity.Property(x => x.City).HasColumnName("city");
            entity.Property(x => x.Price).HasColumnName("price");
            entity.Property(x => x.OldPrice).HasColumnName("old_price");
            entity.Property(x => x.PromoFlag).HasColumnName("promo_flag");
            entity.Property(x => x.InStock).HasColumnName("in_stock");
        });

        modelBuilder.Entity<IngestionRunEntity>(entity =>
        {
            entity.ToTable("ingestion_run");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("ingestion_run_id");
            entity.Property(x => x.CrawlerRunId).HasColumnName("crawler_run_id");
            entity.Property(x => x.StartedAtUtc).HasColumnName("started_at");
            entity.Property(x => x.FinishedAtUtc).HasColumnName("finished_at");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.ErrorCode).HasColumnName("error_code");
            entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
        });

        modelBuilder.Entity<ProductEntity>(entity =>
        {
            entity.ToTable("product");
            entity.HasKey(x => x.ProductKey);

            entity.Property(x => x.ProductKey).HasColumnName("product_key");
            entity.Property(x => x.ProductId).HasColumnName("product_id").HasMaxLength(64);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(512);
            entity.Property(x => x.Url).HasColumnName("url").HasMaxLength(1024);
            entity.Property(x => x.PackValue).HasColumnName("pack_value").HasPrecision(18, 6);
            entity.Property(x => x.PackUnit).HasColumnName("pack_unit").HasMaxLength(16);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at");

            entity.HasIndex(x => x.ProductId).IsUnique();
        });

        modelBuilder.Entity<ProductEntity>(entity =>
        {
            entity.ToTable("product");
            entity.HasKey(x => x.ProductKey);

            entity.Property(x => x.ProductKey).HasColumnName("product_key");
            entity.Property(x => x.ProductId).HasColumnName("product_id").HasMaxLength(64);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(512);
            entity.Property(x => x.Url).HasColumnName("url").HasMaxLength(1024);
            entity.Property(x => x.PackValue).HasColumnName("pack_value").HasPrecision(18, 6);
            entity.Property(x => x.PackUnit).HasColumnName("pack_unit").HasMaxLength(16);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at");

            entity.HasIndex(x => x.ProductId).IsUnique();
        });

        modelBuilder.Entity<ProductErrorsEntity>(entity =>
        {
            entity.ToTable("product");
            entity.HasKey(x => x.ProductKey);

            entity.Property(x => x.ProductKey).HasColumnName("product_key");
            entity.Property(x => x.ProductId).HasColumnName("product_id").HasMaxLength(64);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(512);
            entity.Property(x => x.Url).HasColumnName("url").HasMaxLength(1024);
            entity.Property(x => x.PackValue).HasColumnName("pack_value").HasPrecision(18, 6);
            entity.Property(x => x.PackUnit).HasColumnName("pack_unit").HasMaxLength(16);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at");
            entity.Property(x => x.Url).HasColumnName("error_string").HasMaxLength(256);

            entity.HasIndex(x => x.ProductId).IsUnique();
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

    public DateTime CapturedAtUtc { get; set; }

    public string? City { get; set; }

    public decimal Price { get; set; }

    public decimal? OldPrice { get; set; }

    public bool PromoFlag { get; set; }

    public bool? InStock { get; set; }
}

public sealed class IngestionRunEntity
{
    public long Id { get; set; }

    public long CrawlerRunId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}

public sealed class ProductEntity
{
    public long ProductKey { get; set; }

    public string ProductId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public decimal? PackValue { get; set; }

    public string? PackUnit { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ProductErrorsEntity
{
    public long ProductKey { get; set; }

    public string ProductId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public decimal? PackValue { get; set; }

    public string? PackUnit { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string Error { get; set; }
}
