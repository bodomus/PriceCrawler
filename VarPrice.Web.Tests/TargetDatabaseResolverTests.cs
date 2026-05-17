using Microsoft.Extensions.Configuration;

using VarPrice.Infrastructure.Persistence;

namespace VarPrice.Web.Tests;

public sealed class TargetDatabaseResolverTests
{
    [Fact]
    public void Resolve_WhenTargetIsDev_UsesDevDatabase()
    {
        var database = Resolve(
            target: "Dev",
            devConnectionString: "Host=localhost;Database=varprice;Username=var;Password=myPassword",
            stageConnectionString: "Host=localhost;Database=varprice_stage;Username=var;Password=myPassword");

        Assert.Equal(DatabaseTarget.Dev, database.Target);
        Assert.Equal("varprice", database.DatabaseName);
    }

    [Fact]
    public void Resolve_WhenTargetIsStage_UsesStageDatabase()
    {
        var database = Resolve(
            target: "Stage",
            devConnectionString: "Host=localhost;Database=varprice;Username=var;Password=myPassword",
            stageConnectionString: "Host=localhost;Database=varprice_stage;Username=var;Password=myPassword");

        Assert.Equal(DatabaseTarget.Stage, database.Target);
        Assert.Equal("varprice_stage", database.DatabaseName);
    }

    [Fact]
    public void Resolve_WhenTargetIsInvalid_FailsFast()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(
            target: "Prod",
            devConnectionString: "Host=localhost;Database=varprice;Username=var;Password=myPassword",
            stageConnectionString: "Host=localhost;Database=varprice_stage;Username=var;Password=myPassword"));

        Assert.Contains("Invalid database target 'Prod'", ex.Message);
    }

    [Fact]
    public void Resolve_WhenConnectionStringIsMissing_FailsFast()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Target"] = "Stage",
                ["ConnectionStrings:PostgresDev"] = "Host=localhost;Database=varprice;Username=var;Password=myPassword"
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new TargetDatabaseResolver(config).Resolve());

        Assert.Contains("Connection string 'PostgresStage' is not configured", ex.Message);
    }

    [Fact]
    public void Resolve_WhenTargetDatabaseDoesNotMatchConnectionString_FailsFast()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(
            target: "Stage",
            devConnectionString: "Host=localhost;Database=varprice;Username=var;Password=myPassword",
            stageConnectionString: "Host=localhost;Database=varprice;Username=var;Password=myPassword"));

        Assert.Contains("must use database 'varprice_stage'", ex.Message);
    }

    private static SelectedDatabase Resolve(
        string target,
        string devConnectionString,
        string stageConnectionString)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Target"] = target,
                ["ConnectionStrings:PostgresDev"] = devConnectionString,
                ["ConnectionStrings:PostgresStage"] = stageConnectionString
            })
            .Build();

        return new TargetDatabaseResolver(config).Resolve();
    }
}
