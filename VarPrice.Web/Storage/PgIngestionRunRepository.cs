using System.Data;
using System.Data.Common;

namespace VarPrice.Web.Storage;

public interface IIngestionRunRepository
{
    long StartIngestion(long crawlerRunId, string source);

    Task FinishIngestionAsync(long ingestionRunId, string status, string? note, CancellationToken ct);

    Task FailIngestionAsync(long ingestionRunId, Exception ex, string errorSource, CancellationToken ct);
}

public sealed class PgIngestionRunRepository(IPgConnectionFactory factory) : IIngestionRunRepository
{
    public long StartIngestion(long crawlerRunId, string source)
    {
        using var cn = (DbConnection)factory.Create();
        cn.Open();

        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"
insert into ingestion_run(crawler_run_id, status, source)
values(@crawlerRunId, 'running', @source)
returning run_id;";
        AddParam(cmd, "@crawlerRunId", crawlerRunId);
        AddParam(cmd, "@source", source);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public async Task FinishIngestionAsync(long ingestionRunId, string status, string? note, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = @"
update ingestion_run
set status=@status, note=@note, finished_at=now()
where run_id=@runId;";
        AddParam(cmd, "@status", status);
        AddParam(cmd, "@note", TrimTo(note, 255));
        AddParam(cmd, "@runId", ingestionRunId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task FailIngestionAsync(long ingestionRunId, Exception ex, string errorSource, CancellationToken ct)
    {
        await using var cn = (DbConnection)factory.Create();
        await cn.OpenAsync(ct);

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = @"
update ingestion_run
set status='failed',
    note=@note,
    finished_at=now(),
    error_message=@errorMessage,
    error_details=@errorDetails,
    error_source=@errorSource,
    error_at=now()
where run_id=@runId;";
        AddParam(cmd, "@note", TrimTo(ex.Message, 255));
        AddParam(cmd, "@errorMessage", TrimTo(ex.Message, 2048));
        AddParam(cmd, "@errorDetails", ex.ToString());
        AddParam(cmd, "@errorSource", TrimTo(errorSource, 128));
        AddParam(cmd, "@runId", ingestionRunId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string? TrimTo(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
