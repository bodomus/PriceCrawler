using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PriceCrawler.Infrastructure.DependencyInjection;
using PriceCrawler.Infrastructure.Persistence;

namespace PriceCrawler.Web.Tests;

public sealed class DatabaseSchemaRegistrationTests
{
    public static TheoryData<string, string, DatabaseSchemaStartupMode> HostEnvironmentModes => new()
    {
        { "PriceCrawler.Web", "Development", DatabaseSchemaStartupMode.Ensure },
        { "PriceCrawler.Web", "Test", DatabaseSchemaStartupMode.Ensure },
        { "PriceCrawler.Web", "Stage", DatabaseSchemaStartupMode.ValidateOnly },
        { "PriceCrawler.Web", "Staging", DatabaseSchemaStartupMode.ValidateOnly },
        { "PriceCrawler.Web", "Production", DatabaseSchemaStartupMode.ValidateOnly },
        { "PriceCrawler.Worker", "Development", DatabaseSchemaStartupMode.Ensure },
        { "PriceCrawler.Worker", "Test", DatabaseSchemaStartupMode.Ensure },
        { "PriceCrawler.Worker", "Stage", DatabaseSchemaStartupMode.ValidateOnly },
        { "PriceCrawler.Worker", "Staging", DatabaseSchemaStartupMode.ValidateOnly },
        { "PriceCrawler.Worker", "Production", DatabaseSchemaStartupMode.ValidateOnly }
    };

    [Fact]
    public void AddPriceCrawlerInfrastructure_RegistersSeparatedSchemaServicesAndBindsMode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=registration_test;Username=test",
                ["DatabaseSchema:StartupMode"] = "Ensure"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddPriceCrawlerInfrastructure(configuration);

        AssertScoped<DatabaseSchemaInitializer>(services);
        AssertScoped<DatabaseSchemaVersionReader>(services);
        AssertScoped<DatabaseSchemaValidator>(services);
        AssertScoped<DatabaseSchemaStartupCoordinator>(services);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DatabaseSchemaOptions>>().Value;
        Assert.Equal(DatabaseSchemaStartupMode.Ensure, options.StartupMode);
        Assert.Equal([nameof(DatabaseSchemaOptions.StartupMode)],
            typeof(DatabaseSchemaOptions).GetProperties().Select(property => property.Name));
    }

    [Theory]
    [MemberData(nameof(HostEnvironmentModes))]
    public void HostConfiguration_MapsExpectedStartupMode(
        string hostDirectory,
        string environmentName,
        DatabaseSchemaStartupMode expectedMode)
    {
        var root = ResolveRepositoryRoot();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(root, hostDirectory))
            .AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.{environmentName}.json")
            .Build();

        var options = configuration.GetSection(DatabaseSchemaOptions.SectionName).Get<DatabaseSchemaOptions>();

        Assert.NotNull(options);
        Assert.Equal(expectedMode, options.StartupMode);
    }

    [Theory]
    [InlineData("Development", DatabaseSchemaStartupMode.Ensure)]
    [InlineData("Test", DatabaseSchemaStartupMode.Ensure)]
    [InlineData("Stage", DatabaseSchemaStartupMode.ValidateOnly)]
    [InlineData("Staging", DatabaseSchemaStartupMode.ValidateOnly)]
    [InlineData("Production", DatabaseSchemaStartupMode.ValidateOnly)]
    [InlineData("Unexpected", DatabaseSchemaStartupMode.ValidateOnly)]
    public void StartupPolicy_DeclaresSafeEnvironmentDefault(
        string environmentName,
        DatabaseSchemaStartupMode expectedMode)
        => Assert.Equal(expectedMode, DatabaseSchemaStartupPolicy.GetDefaultMode(environmentName));

    [Theory]
    [InlineData("Stage")]
    [InlineData("Staging")]
    [InlineData("Production")]
    [InlineData("Unexpected")]
    public void StartupPolicy_RejectsEnsureOutsideDevelopmentAndTest(string environmentName)
    {
        var error = Assert.Throws<DatabaseSchemaStartupConfigurationException>(() =>
            DatabaseSchemaStartupPolicy.EnsureSafe(environmentName, DatabaseSchemaStartupMode.Ensure));

        Assert.Equal(environmentName, error.EnvironmentName);
        Assert.Equal(DatabaseSchemaStartupMode.Ensure, error.ConfiguredMode);
        Assert.Equal(DatabaseSchemaStartupMode.ValidateOnly, error.RequiredMode);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void StartupPolicy_AllowsEnsureOnlyForInitializationEnvironments(string environmentName)
        => DatabaseSchemaStartupPolicy.EnsureSafe(environmentName, DatabaseSchemaStartupMode.Ensure);

    [Fact]
    public void HighestPrecedenceOverride_CannotBypassProductionSafetyPolicy()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSchema:StartupMode"] = "ValidateOnly"
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSchema:StartupMode"] = "Ensure"
            })
            .Build();
        var options = configuration.GetSection(DatabaseSchemaOptions.SectionName).Get<DatabaseSchemaOptions>();

        Assert.NotNull(options);
        Assert.Equal(DatabaseSchemaStartupMode.Ensure, options.StartupMode);
        Assert.Throws<DatabaseSchemaStartupConfigurationException>(() =>
            DatabaseSchemaStartupPolicy.EnsureSafe("Production", options.StartupMode));
    }

    private static void AssertScoped<TService>(IEnumerable<ServiceDescriptor> services)
        => Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(TService)
            && descriptor.Lifetime == ServiceLifetime.Scoped);

    private static string ResolveRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PriceCrawler.sln"))) return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
