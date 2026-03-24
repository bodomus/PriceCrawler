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

    public DbSet<CrawlErrorEntity> CrawlErrors => Set<CrawlErrorEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CrawlerRunEntity>(entity =>
        {
            entity.ToTable("crawler_run");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Source, x.StartedAtUtc })
                .IsDescending(false, true)
                .HasDatabaseName("ix_crawler_run_source_started_at_desc");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StartedAtUtc).HasColumnName("started_at");
            entity.Property(x => x.FinishedAtUtc).HasColumnName("finished_at");
            entity.Property(x => x.Status)
                .HasColumnName("status")
                .HasMaxLength(32)
                .HasConversion(
                    status => status == RunStatus.Running
                        ? "running"
                        : status == RunStatus.Ok
                            ? "ok"
                            : "error",
                    value => value == "running"
                        ? RunStatus.Running
                        : value == "ok"
                            ? RunStatus.Ok
                            : RunStatus.Error);
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
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.RunId, x.Url }).IsUnique().HasDatabaseName("ux_price_collect_queue_run_url");
            entity.HasIndex(x => x.IdempotencyKey)
                .IsUnique()
                .HasFilter("idempotency_key is not null")
                .HasDatabaseName("ux_price_collect_queue_idempotency");
            entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.Id })
                .HasDatabaseName("ix_price_collect_queue_pick");
            entity.HasIndex(x => new { x.Status, x.LeaseUntilUtc }).HasDatabaseName("ix_price_collect_queue_lease");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.RunId).HasColumnName("run_id");
            entity.Property(x => x.Url).HasColumnName("url").HasMaxLength(1024);
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
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Url).IsUnique().HasDatabaseName("ux_product_url");
            entity.HasIndex(x => x.ExternalId).HasDatabaseName("ix_product_external_id");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(64);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(512);
            entity.Property(x => x.Url).HasColumnName("url").HasMaxLength(1024);
            entity.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(512);
            entity.Property(x => x.PackValue).HasColumnName("pack_value").HasPrecision(18, 6);
            entity.Property(x => x.PackUnit).HasColumnName("pack_unit").HasMaxLength(16);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at");
        });

        modelBuilder.Entity<PriceSnapshotEntity>(entity =>
        {
            entity.ToTable("price_snapshot");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ProductId, x.CapturedAtUtc })
                .IsDescending(false, true)
                .HasDatabaseName("ix_price_snapshot_product_captured_at_desc");
            entity.HasIndex(x => x.RunId).HasDatabaseName("ix_price_snapshot_run_id");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.RunId).HasColumnName("run_id");
            entity.Property(x => x.ProductId).HasColumnName("product_id");
            entity.Property(x => x.CapturedAtUtc).HasColumnName("captured_at");
            entity.Property(x => x.Price).HasColumnName("price").HasPrecision(18, 2);
            entity.Property(x => x.OldPrice).HasColumnName("old_price").HasPrecision(18, 2);
            entity.Property(x => x.PromoFlag).HasColumnName("promo_flag");
            entity.Property(x => x.InStock).HasColumnName("in_stock");
            entity.Property(x => x.QueueId).HasColumnName("queue_id");
        });

        modelBuilder.Entity<CrawlErrorEntity>(entity =>
        {
            entity.ToTable("crawl_error");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.RunId).HasDatabaseName("ix_crawl_error_run_id");
            entity.HasIndex(x => x.ProductId).HasDatabaseName("ix_crawl_error_product_id");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.RunId).HasColumnName("run_id");
            entity.Property(x => x.QueueId).HasColumnName("queue_id");
            entity.Property(x => x.ProductId).HasColumnName("product_id");
            entity.Property(x => x.Url).HasColumnName("url").HasMaxLength(1024);
            entity.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(64);
            entity.Property(x => x.HttpStatus).HasColumnName("http_status");
            entity.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(512);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at");
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
    public long Id { get; set; }

    public long RunId { get; set; }

    public string Url { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int Attempt { get; set; }

    public int MaxAttempts { get; set; }

    public DateTime? NextAttemptAtUtc { get; set; }

    public DateTime? ReservedAtUtc { get; set; }

    public DateTime? LeaseUntilUtc { get; set; }

    public string? ReservedBy { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? LastErrorCode { get; set; }

    public int? LastHttpStatus { get; set; }

    public string? LastErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }
}

public sealed class ProductEntity
{
    public long Id { get; set; }

    public string? ExternalId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? Slug { get; set; }

    public decimal? PackValue { get; set; }

    public string? PackUnit { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class PriceSnapshotEntity
{
    public long Id { get; set; }

    public long RunId { get; set; }

    public long ProductId { get; set; }

    public DateTime CapturedAtUtc { get; set; }

    public decimal? Price { get; set; }

    public decimal? OldPrice { get; set; }

    public bool PromoFlag { get; set; }

    public bool InStock { get; set; }

    public long? QueueId { get; set; }
}

public sealed class CrawlErrorEntity
{
    public long Id { get; set; }

    public long RunId { get; set; }

    public long? QueueId { get; set; }

    public long? ProductId { get; set; }

    public string? Url { get; set; }

    public string? ErrorCode { get; set; }

    public int? HttpStatus { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
