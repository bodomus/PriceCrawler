using Microsoft.Extensions.Configuration;

namespace VarPrice.Infrastructure.Persistence;

public sealed class StageSafetyGuard(SelectedDatabase database, IConfiguration configuration)
{
    public const string AllowStageSchemaBootstrapKey = "Database:AllowStageSchemaBootstrap";

    public bool ShouldRunStartupSchemaBootstrap()
        => database.Target != DatabaseTarget.Stage || IsStageSchemaBootstrapAllowed();

    public void EnsureSchemaBootstrapAllowed()
    {
        if (database.Target != DatabaseTarget.Stage || IsStageSchemaBootstrapAllowed())
        {
            return;
        }

        throw new InvalidOperationException(
            $"Schema bootstrap is disabled for database target '{DatabaseTarget.Stage}'. " +
            $"Set '{AllowStageSchemaBootstrapKey}=true' only for an intentional, reviewed stage schema update.");
    }

    public void EnsureDestructiveOperationAllowed(string operationName)
    {
        if (database.Target != DatabaseTarget.Stage)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Operation '{operationName}' is blocked for database target '{DatabaseTarget.Stage}' because it can modify or delete stage data.");
    }

    private bool IsStageSchemaBootstrapAllowed()
    {
        var rawValue = configuration[AllowStageSchemaBootstrapKey];
        return string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);
    }
}
