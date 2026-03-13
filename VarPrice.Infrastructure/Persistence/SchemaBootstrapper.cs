using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace VarPrice.Infrastructure.Persistence;

public sealed class SchemaBootstrapper(VarPriceDbContext dbContext, ILogger<SchemaBootstrapper> log)
{
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        const int attempts = 30;
        const int delayMs = 1000;
        Exception? last = null;

        for (var i = 1; i <= attempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await dbContext.Database.MigrateAsync(ct);

                log.LogInformation("Schema ensured");
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                log.LogWarning(ex, "Postgres not ready (attempt {Attempt}/{Attempts})", i, attempts);
                await Task.Delay(delayMs, ct);
            }
        }

        throw new InvalidOperationException("Failed to ensure schema (Postgres not ready)", last);
    }
}
