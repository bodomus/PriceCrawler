using Microsoft.Extensions.Configuration;

using VarPrice.Infrastructure.Persistence;

namespace VarPrice.Web.Tests;

public sealed class StageSafetyGuardTests
{
    [Fact]
    public void ShouldRunStartupSchemaBootstrap_WhenTargetIsDev_ReturnsTrue()
    {
        var guard = CreateGuard(DatabaseTarget.Dev);

        Assert.True(guard.ShouldRunStartupSchemaBootstrap());
    }

    [Fact]
    public void ShouldRunStartupSchemaBootstrap_WhenTargetIsStage_ReturnsFalseByDefault()
    {
        var guard = CreateGuard(DatabaseTarget.Stage);

        Assert.False(guard.ShouldRunStartupSchemaBootstrap());
    }

    [Fact]
    public void ShouldRunStartupSchemaBootstrap_WhenTargetIsStageAndExplicitlyAllowed_ReturnsTrue()
    {
        var guard = CreateGuard(DatabaseTarget.Stage, allowStageSchemaBootstrap: "true");

        Assert.True(guard.ShouldRunStartupSchemaBootstrap());
    }

    [Fact]
    public void EnsureSchemaBootstrapAllowed_WhenTargetIsStageAndNotAllowed_FailsFast()
    {
        var guard = CreateGuard(DatabaseTarget.Stage);

        var ex = Assert.Throws<InvalidOperationException>(() => guard.EnsureSchemaBootstrapAllowed());

        Assert.Contains("Schema bootstrap is disabled", ex.Message);
        Assert.Contains(StageSafetyGuard.AllowStageSchemaBootstrapKey, ex.Message);
    }

    [Fact]
    public void EnsureDestructiveOperationAllowed_WhenTargetIsStage_FailsFast()
    {
        var guard = CreateGuard(DatabaseTarget.Stage);

        var ex =
            Assert.Throws<InvalidOperationException>(() => guard.EnsureDestructiveOperationAllowed("EnsureDeleted"));

        Assert.Contains("EnsureDeleted", ex.Message);
        Assert.Contains("blocked for database target 'Stage'", ex.Message);
    }

    private static StageSafetyGuard CreateGuard(
        DatabaseTarget target,
        string? allowStageSchemaBootstrap = null)
    {
        var configurationValues = new Dictionary<string, string?>();
        if (allowStageSchemaBootstrap is not null)
        {
            configurationValues[StageSafetyGuard.AllowStageSchemaBootstrapKey] = allowStageSchemaBootstrap;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();

        var databaseName = target == DatabaseTarget.Stage ? "varprice_stage" : "varprice";
        return new StageSafetyGuard(
            new SelectedDatabase(target, $"Host=localhost;Database={databaseName}", databaseName),
            configuration);
    }
}
