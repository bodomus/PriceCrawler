using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PriceCrawler.Infrastructure.DependencyInjection;
using PriceCrawler.Infrastructure.Persistence;

namespace PriceCrawler.Web.Tests;

public sealed class DatabaseSchemaRegistrationTests
{
    [Fact]
    public void AddPriceCrawlerInfrastructure_RegistersSchemaServicesAndBindsOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=registration_test;Username=test",
                ["DatabaseSchema:AllowAutomaticInitialization"] = "true",
                ["DatabaseSchema:ValidateOnStartup"] = "false"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddPriceCrawlerInfrastructure(configuration);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(DatabaseSchemaVersionReader)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(DatabaseSchemaStartupService)
            && descriptor.Lifetime == ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DatabaseSchemaOptions>>().Value;
        Assert.True(options.AllowAutomaticInitialization);
        Assert.False(options.ValidateOnStartup);
    }
}
