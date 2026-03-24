#nullable disable

#if false
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

using VarPrice.Domain.Enums;

namespace VarPrice.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VarPriceDbContext))]
partial class VarPriceDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "8.0.8")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("VarPrice.Infrastructure.Persistence.CrawlerRunEntity", b =>
        {
            b.Property<long>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint")
                .HasColumnName("run_id")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            b.Property<DateTime?>("FinishedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("finished_at");

            b.Property<string>("Note")
                .HasMaxLength(255)
                .HasColumnType("character varying(255)")
                .HasColumnName("note");

            b.Property<string>("Source")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("character varying(64)")
                .HasColumnName("source");

            b.Property<DateTime>("StartedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("started_at");

            b.Property<RunStatus>("Status")
                .HasConversion(new EnumToNumberConverter<RunStatus, short>())
                .HasColumnType("smallint")
                .HasColumnName("status");

            b.HasKey("Id");

            b.HasIndex("Source", "StartedAtUtc")
                .IsDescending(false, true)
                .HasDatabaseName("ix_crawler_run_source_started_at_desc");

            b.ToTable("crawler_run", (string)null);
        });

        modelBuilder.Entity("VarPrice.Infrastructure.Persistence.IngestionRunEntity", b =>
        {
            b.Property<long>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint")
                .HasColumnName("ingestion_run_id")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            b.Property<long>("CrawlerRunId")
                .HasColumnType("bigint")
                .HasColumnName("crawler_run_id");

            b.Property<string>("ErrorCode")
                .HasMaxLength(128)
                .HasColumnType("character varying(128)")
                .HasColumnName("error_code");

            b.Property<string>("ErrorMessage")
                .HasMaxLength(512)
                .HasColumnType("character varying(512)")
                .HasColumnName("error_message");

            b.Property<DateTime?>("FinishedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("finished_at");

            b.Property<DateTime>("StartedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("started_at");

            b.Property<string>("Status")
                .IsRequired()
                .HasMaxLength(32)
                .HasColumnType("character varying(32)")
                .HasColumnName("status");

            b.HasKey("Id");

            b.ToTable("ingestion_run", (string)null);
        });

        modelBuilder.Entity("VarPrice.Infrastructure.Persistence.PriceCollectQueueEntity", b =>
        {
            b.Property<long>("QueueId")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint")
                .HasColumnName("queue_id")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            b.Property<int>("Attempt")
                .HasColumnType("integer")
                .HasColumnName("attempt");

            b.Property<DateTime>("CreatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at");

            b.Property<string>("City")
                .HasMaxLength(128)
                .HasColumnType("character varying(128)")
                .HasColumnName("city");

            b.Property<DateTime?>("FinishedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("finished_at");

            b.Property<string>("IdempotencyKey")
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnType("character varying(128)")
                .HasColumnName("idempotency_key");

            b.Property<string>("LastErrorCode")
                .HasMaxLength(64)
                .HasColumnType("character varying(64)")
                .HasColumnName("last_error_code");

            b.Property<string>("LastErrorMessage")
                .HasMaxLength(512)
                .HasColumnType("character varying(512)")
                .HasColumnName("last_error_message");

            b.Property<int?>("LastHttpStatus")
                .HasColumnType("integer")
                .HasColumnName("last_http_status");

            b.Property<DateTime?>("LeaseUntilUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("lease_until");

            b.Property<int>("MaxAttempts")
                .HasColumnType("integer")
                .HasColumnName("max_attempts");

            b.Property<DateTime>("NextAttemptAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("next_attempt_at");

            b.Property<DateTime?>("ReservedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("reserved_at");

            b.Property<string>("ReservedBy")
                .HasMaxLength(128)
                .HasColumnType("character varying(128)")
                .HasColumnName("reserved_by");

            b.Property<long>("RunId")
                .HasColumnType("bigint")
                .HasColumnName("run_id");

            b.Property<string>("Status")
                .IsRequired()
                .HasMaxLength(32)
                .HasColumnType("character varying(32)")
                .HasColumnName("status");

            b.Property<DateTime>("UpdatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("updated_at");

            b.Property<string>("Url")
                .IsRequired()
                .HasMaxLength(1024)
                .HasColumnType("character varying(1024)")
                .HasColumnName("url");

            b.HasKey("QueueId");

            b.HasIndex("IdempotencyKey")
                .IsUnique()
                .HasDatabaseName("ux_price_collect_queue_idempotency");

            b.HasIndex("RunId", "Url")
                .IsUnique()
                .HasDatabaseName("ux_price_collect_queue_run_url");

            b.HasIndex("Status", "LeaseUntilUtc")
                .HasDatabaseName("ix_price_collect_queue_lease");

            b.HasIndex("Status", "NextAttemptAtUtc", "QueueId")
                .HasDatabaseName("ix_price_collect_queue_pick");

            b.ToTable("price_collect_queue", (string)null);
        });

        modelBuilder.Entity("VarPrice.Infrastructure.Persistence.PriceSnapshotEntity", b =>
        {
            b.Property<long>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint")
                .HasColumnName("snapshot_id")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            b.Property<DateTime>("CapturedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("captured_at");

            b.Property<string>("City")
                .HasMaxLength(128)
                .HasColumnType("character varying(128)")
                .HasColumnName("city");

            b.Property<int?>("DiscountPercent")
                .HasColumnType("integer")
                .HasColumnName("discount_percent");

            b.Property<decimal?>("FinalPrice")
                .HasPrecision(18, 2)
                .HasColumnType("numeric(18,2)")
                .HasColumnName("final_price");

            b.Property<bool?>("InStock")
                .HasColumnType("boolean")
                .HasColumnName("in_stock");

            b.Property<long>("ProductKey")
                .HasColumnType("bigint")
                .HasColumnName("product_key");

            b.Property<bool>("PromoFlag")
                .HasColumnType("boolean")
                .HasColumnName("promo_flag");

            b.Property<long?>("QueueId")
                .HasColumnType("bigint")
                .HasColumnName("queue_id");

            b.Property<decimal?>("RegularPrice")
                .HasPrecision(18, 2)
                .HasColumnType("numeric(18,2)")
                .HasColumnName("regular_price");

            b.Property<long>("RunId")
                .HasColumnType("bigint")
                .HasColumnName("run_id");

            b.HasKey("Id");

            b.HasIndex("ProductKey", "CapturedAtUtc")
                .IsDescending(false, true)
                .HasDatabaseName("ix_price_snapshot_product_captured_at_desc");

            b.HasIndex("RunId")
                .HasDatabaseName("ix_price_snapshot_run_id");

            b.ToTable("price_snapshot", (string)null);
        });

        modelBuilder.Entity("VarPrice.Infrastructure.Persistence.ProductEntity", b =>
        {
            b.Property<long>("ProductKey")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint")
                .HasColumnName("product_key")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            b.Property<DateTime>("CreatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at");

            b.Property<DateTime?>("LastSeenAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("last_seen_at");

            b.Property<string>("Name")
                .IsRequired()
                .HasMaxLength(512)
                .HasColumnType("character varying(512)")
                .HasColumnName("name");

            b.Property<decimal?>("PackValue")
                .HasPrecision(18, 6)
                .HasColumnType("numeric(18,6)")
                .HasColumnName("pack_value");

            b.Property<string>("PackUnit")
                .HasMaxLength(16)
                .HasColumnType("character varying(16)")
                .HasColumnName("pack_unit");

            b.Property<string>("ProductId")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("character varying(64)")
                .HasColumnName("product_id");

            b.Property<string>("Url")
                .IsRequired()
                .HasMaxLength(1024)
                .HasColumnType("character varying(1024)")
                .HasColumnName("url");

            b.HasKey("ProductKey");

            b.HasIndex("ProductId")
                .IsUnique();

            b.ToTable("product", (string)null);
        });

        modelBuilder.Entity("VarPrice.Infrastructure.Persistence.ProductErrorEntity", b =>
        {
            b.Property<long>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint")
                .HasColumnName("product_error_id")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            b.Property<string>("DetailsJson")
                .HasColumnType("jsonb")
                .HasColumnName("details_json");

            b.Property<string>("ErrorCode")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("character varying(64)")
                .HasColumnName("error_code");

            b.Property<string>("ErrorMessage")
                .IsRequired()
                .HasMaxLength(512)
                .HasColumnType("character varying(512)")
                .HasColumnName("error_message");

            b.Property<DateTime>("OccurredAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("occurred_at");

            b.Property<long?>("PriceSnapshotId")
                .HasColumnType("bigint")
                .HasColumnName("price_snapshot_id");

            b.Property<long?>("ProductKey")
                .HasColumnType("bigint")
                .HasColumnName("product_key");

            b.Property<long?>("QueueId")
                .HasColumnType("bigint")
                .HasColumnName("queue_id");

            b.Property<long>("RunId")
                .HasColumnType("bigint")
                .HasColumnName("run_id");

            b.Property<string>("Stage")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("character varying(64)")
                .HasColumnName("stage");

            b.HasKey("Id");

            b.HasIndex("ProductKey")
                .HasDatabaseName("ix_product_errors_product_key");

            b.HasIndex("RunId")
                .HasDatabaseName("ix_product_errors_run_id");

            b.ToTable("product_errors", (string)null);
        });
#pragma warning restore 612, 618
    }
}
#endif
