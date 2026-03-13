using Microsoft.EntityFrameworkCore;

using VarPrice.Domain.Enums;

namespace VarPrice.Infrastructure.Persistence;

public sealed class VarPriceDbContext(DbContextOptions<VarPriceDbContext> options) : DbContext(options)
{
    public DbSet<CrawlerRunEntity> CrawlerRuns => Set<CrawlerRunEntity>();

    public DbSet<IngestionRunEntity> IngestionRuns => Set<IngestionRunEntity>();

    public DbSet<PriceCollectQueueEntity> PriceCollectQueueItems => Set<PriceCollectQueueEntity>();

    public DbSet<ProductEntity> Products => Set<ProductEntity>();

    public DbSet<PriceSnapshotEntity> PriceSnapshots => Set<PriceSnapshotEntity>();

    public DbSet<ProductErrorEntity> ProductErrors => Set<ProductErrorEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CrawlerRunEntity>(entity =>
        {
            entity.ToTable("crawler_run");
            entity.HasKey(x => x.Id);
            entity.ToTable(t => t.HasCheckConstraint("ck_crawler_run_status", "status in (0,1,2)"));
            entity.HasIndex(x => new { x.Source, x.StartedAtUtc })
                .IsDescending(false, true)
                .HasDatabaseName("ix_crawler_run_source_started_at_desc");

            entity.Property(x => x.Id).HasColumnName("run_id");
            entity.Property(x => x.StartedAtUtc).HasColumnName("started_at");
            entity.Property(x => x.FinishedAtUtc).HasColumnName("finished_at");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            entity.Property(x => x.Source).HasColumnName("source").HasMaxLength(64);
            entity.Property(x => x.Note).HasColumnName("note").HasMaxLength(255);
        });

        modelBuilder.Entity<IngestionRunEntity>(entity =>
        {
            entity.ToTable("ingestion_run");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("ingestion_run_id");
            entity.Property(x => x.CrawlerRunId).HasColumnName("crawler_run_id");
            entity.Property(x => x.StartedAtUtc).HasColumnName("started_at");
            entity.Property(x => x.FinishedAtUtc).HasColumnName("finished_at");
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
            entity.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(512);
        });

        modelBuilder.Entity<PriceCollectQueueEntity>(entity =>
        {
            entity.ToTable("price_collect_queue");
            entity.HasKey(x => x.QueueId);

            entity.HasIndex(x => new { x.RunId, x.Url }).IsUnique().HasDatabaseName("ux_price_collect_queue_run_url");
            entity.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("ux_price_collect_queue_idempotency");
            entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.QueueId })
                .HasDatabaseName("ix_price_collect_queue_pick");
            entity.HasIndex(x => new { x.Status, x.LeaseUntilUtc }).HasDatabaseName("ix_price_collect_queue_lease");

            entity.Property(x => x.QueueId).HasColumnName("queue_id");
            entity.Property(x => x.RunId).HasColumnName("run_id");
            entity.Property(x => x.Url).HasColumnName("url").HasMaxLength(1024);
            entity.Property(x => x.City).HasColumnName("city").HasMaxLength(128);
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(x => x.Attempt).HasColumnName("attempt");
            entity.Property(x => x.MaxAttempts).HasColumnName("max_attempts");
            entity.Property(x => x.NextAttemptAtUtc).HasColumnName("next_attempt_at");
            entity.Property(x => x.ReservedAtUtc).HasColumnName("reserved_at");
            entity.Property(x => x.LeaseUntilUtc).HasColumnName("lease_until");
            entity.Property(x => x.ReservedBy).HasColumnName("reserved_by").HasMaxLength(128);
            entity.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
            entity.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(64);
            entity.Property(x => x.LastHttpStatus).HasColumnName("last_http_status");
            entity.Property(x => x.LastErrorMessage).HasColumnName("last_error_message").HasMaxLength(512);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at");
            entity.Property(x => x.FinishedAtUtc).HasColumnName("finished_at");
        });

        modelBuilder.Entity<ProductEntity>(entity =>
        {
            entity.ToTable("product");
            entity.HasKey(x => x.ProductKey);
            entity.HasIndex(x => x.ProductId).IsUnique();

            entity.Property(x => x.ProductKey).HasColumnName("product_key");
            entity.Property(x => x.ProductId).HasColumnName("product_id").HasMaxLength(64);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(512);
            entity.Property(x => x.Url).HasColumnName("url").HasMaxLength(1024);
            entity.Property(x => x.PackValue).HasColumnName("pack_value").HasPrecision(18, 6);
            entity.Property(x => x.PackUnit).HasColumnName("pack_unit").HasMaxLength(16);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at");
            entity.Property(x => x.LastSeenAtUtc).HasColumnName("last_seen_at");
        });

        modelBuilder.Entity<PriceSnapshotEntity>(entity =>
        {
            entity.ToTable("price_snapshot");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ProductKey, x.CapturedAtUtc })
                .IsDescending(false, true)
                .HasDatabaseName("ix_price_snapshot_product_captured_at_desc");
            entity.HasIndex(x => x.RunId).HasDatabaseName("ix_price_snapshot_run_id");

            entity.Property(x => x.Id).HasColumnName("snapshot_id");
            entity.Property(x => x.QueueId).HasColumnName("queue_id");
            entity.Property(x => x.RunId).HasColumnName("run_id");
            entity.Property(x => x.CapturedAtUtc).HasColumnName("captured_at");
            entity.Property(x => x.ProductKey).HasColumnName("product_key");
            entity.Property(x => x.City).HasColumnName("city").HasMaxLength(128);
            entity.Property(x => x.RegularPrice).HasColumnName("regular_price").HasPrecision(18, 2);
            entity.Property(x => x.FinalPrice).HasColumnName("final_price").HasPrecision(18, 2);
            entity.Property(x => x.DiscountPercent).HasColumnName("discount_percent");
            entity.Property(x => x.PromoFlag).HasColumnName("promo_flag");
            entity.Property(x => x.InStock).HasColumnName("in_stock");
        });

        modelBuilder.Entity<ProductErrorEntity>(entity =>
        {
            entity.ToTable("product_errors");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.RunId).HasDatabaseName("ix_product_errors_run_id");
            entity.HasIndex(x => x.ProductKey).HasDatabaseName("ix_product_errors_product_key");

            entity.Property(x => x.Id).HasColumnName("product_error_id");
            entity.Property(x => x.RunId).HasColumnName("run_id");
            entity.Property(x => x.ProductKey).HasColumnName("product_key");
            entity.Property(x => x.PriceSnapshotId).HasColumnName("price_snapshot_id");
            entity.Property(x => x.QueueId).HasColumnName("queue_id");
            entity.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at");
            entity.Property(x => x.Stage).HasColumnName("stage").HasMaxLength(64);
            entity.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(64);
            entity.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(512);
            entity.Property(x => x.DetailsJson).HasColumnName("details_json").HasColumnType("jsonb");
        });
    }
}

public sealed class CrawlerRunEntity
{
    public long Id { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    public RunStatus Status { get; set; }

    public string Source { get; set; } = string.Empty;

    public string? Note { get; set; }
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

public sealed class PriceCollectQueueEntity
{
    public long QueueId { get; set; }

    public long RunId { get; set; }

    public string Url { get; set; } = string.Empty;

    public string? City { get; set; }

    public string Status { get; set; } = string.Empty;

    public int Attempt { get; set; }

    public int MaxAttempts { get; set; }

    public DateTime NextAttemptAtUtc { get; set; }

    public DateTime? ReservedAtUtc { get; set; }

    public DateTime? LeaseUntilUtc { get; set; }

    public string? ReservedBy { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string? LastErrorCode { get; set; }

    public int? LastHttpStatus { get; set; }

    public string? LastErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }
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

    public DateTime? LastSeenAtUtc { get; set; }
}

public sealed class PriceSnapshotEntity
{
    public long Id { get; set; }

    public long? QueueId { get; set; }

    public long RunId { get; set; }

    public DateTime CapturedAtUtc { get; set; }

    public long ProductKey { get; set; }

    public string? City { get; set; }

    public decimal? RegularPrice { get; set; }

    public decimal? FinalPrice { get; set; }

    public int? DiscountPercent { get; set; }

    public bool PromoFlag { get; set; }

    public bool? InStock { get; set; }
}

public sealed class ProductErrorEntity
{
    public long Id { get; set; }

    public long RunId { get; set; }

    public long? ProductKey { get; set; }

    public long? PriceSnapshotId { get; set; }

    public long? QueueId { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public string Stage { get; set; } = string.Empty;

    public string ErrorCode { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public string? DetailsJson { get; set; }
}
