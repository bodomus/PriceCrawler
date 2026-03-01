using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace VarPrice.Web.Storage.Db;

public sealed class DbExecutor(
    DbErrorMapper errorMapper,
    IHttpContextAccessor httpContextAccessor,
    ILogger<DbExecutor> logger)
{
    public async Task<DbResult<T>> ExecuteAsync<T>(
        Func<Task<T>> action,
        string operation,
        string? entity = null,
        string? entityId = null,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        try
        {
            var value = await action();
            return DbResult<T>.Success(value);
        }
        catch (Exception ex)
        {
            var correlationId = ResolveCorrelationId();
            var error = errorMapper.Map(ex, operation, correlationId);
            var sanitizedParams = SanitizeParameters(parameters);

            logger.LogError(
                ex,
                "DB operation failed. Operation={Operation} Entity={Entity} EntityId={EntityId} CorrelationId={CorrelationId} ErrorCode={ErrorCode} Params={Params}",
                operation,
                entity,
                entityId,
                correlationId,
                error.Code,
                sanitizedParams);

            return DbResult<T>.Fail(error);
        }
    }

    public async Task<DbResult> ExecuteAsync(
        Func<Task> action,
        string operation,
        string? entity = null,
        string? entityId = null,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        var result = await ExecuteAsync(
            async () =>
            {
                await action();
                return true;
            },
            operation,
            entity,
            entityId,
            parameters);

        return result.IsSuccess ? DbResult.Success() : DbResult.Fail(result.Error!);
    }

    private string ResolveCorrelationId()
    {
        var traceIdentifier = httpContextAccessor.HttpContext?.TraceIdentifier;
        if (!string.IsNullOrWhiteSpace(traceIdentifier))
        {
            return traceIdentifier;
        }

        if (!string.IsNullOrWhiteSpace(Activity.Current?.Id))
        {
            return Activity.Current!.Id!;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static IReadOnlyDictionary<string, object?> SanitizeParameters(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return new Dictionary<string, object?>();
        }

        var sanitized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parameters)
        {
            if (IsSensitiveKey(key))
            {
                sanitized[key] = "***";
                continue;
            }

            sanitized[key] = value;
        }

        return sanitized;
    }

    private static bool IsSensitiveKey(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("pwd", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("connection", StringComparison.OrdinalIgnoreCase);
}
