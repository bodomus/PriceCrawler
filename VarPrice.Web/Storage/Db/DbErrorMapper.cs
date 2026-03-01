using System.Data.Common;
using Npgsql;

namespace VarPrice.Web.Storage.Db;

public sealed class DbErrorMapper
{
    public DbError Map(Exception exception, string operation, string correlationId)
    {
        var code = MapCode(exception);
        var userMessage = MapUserMessage(code);
        var technicalDetails = SanitizeTechnicalDetails(exception.Message);

        return new DbError(
            Code: code,
            UserMessage: userMessage,
            TechnicalDetails: technicalDetails,
            Operation: operation,
            CorrelationId: correlationId
        );
    }

    private static string MapCode(Exception exception)
    {
        if (IsConstraintViolation(exception))
        {
            return DbErrorCodes.Constraint;
        }

        if (exception is TimeoutException)
        {
            return DbErrorCodes.Timeout;
        }

        if (exception is NpgsqlException npgsqlEx && npgsqlEx.InnerException is TimeoutException)
        {
            return DbErrorCodes.Timeout;
        }

        if (exception is NpgsqlException or DbException)
        {
            return DbErrorCodes.Connection;
        }

        if (exception.GetType().Name == "DbUpdateException")
        {
            return DbErrorCodes.Constraint;
        }

        return DbErrorCodes.Unknown;
    }

    private static bool IsConstraintViolation(Exception exception)
    {
        if (!TryGetSqlState(exception, out var sqlState))
        {
            return false;
        }

        return sqlState is "23505" or "23503" or "23502" or "23514" or "23P01";
    }

    private static bool TryGetSqlState(Exception exception, out string? sqlState)
    {
        sqlState = null;
        var property = exception.GetType().GetProperty("SqlState");
        if (property?.PropertyType != typeof(string))
        {
            return false;
        }

        sqlState = property.GetValue(exception) as string;
        return !string.IsNullOrWhiteSpace(sqlState);
    }

    private static string MapUserMessage(string code) =>
        code switch
        {
            DbErrorCodes.Constraint => "Запись уже существует или нарушены ограничения данных.",
            DbErrorCodes.Connection => "Не удалось подключиться к базе данных.",
            DbErrorCodes.Timeout => "Операция с базой данных превысила время ожидания.",
            _ => "Не удалось выполнить операцию с базой данных."
        };

    private static string SanitizeTechnicalDetails(string details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return string.Empty;
        }

        var sanitized = details
            .Replace("Password=", "Password=***", StringComparison.OrdinalIgnoreCase)
            .Replace("Pwd=", "Pwd=***", StringComparison.OrdinalIgnoreCase)
            .Replace("Token=", "Token=***", StringComparison.OrdinalIgnoreCase)
            .Replace("User ID=", "User ID=***", StringComparison.OrdinalIgnoreCase);

        return sanitized;
    }
}
