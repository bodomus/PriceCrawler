using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using PriceCrawler.Api.Startup;
using PriceCrawler.Domain.Interfaces;

namespace PriceCrawler.Api.Tests;

/// <summary>Hosts the API in memory without a database: categories come from <see cref="FakeCategoryRepository"/>.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public FakeCategoryRepository Categories { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICategoryRepository>();
            services.AddSingleton<ICategoryRepository>(Categories);

            ServiceDescriptor schemaCheck = services.Single(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(DatabaseSchemaStartupService));
            services.Remove(schemaCheck);
        });
    }
}
