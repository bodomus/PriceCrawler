using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;
using VarPrice.Application.UseCases;

namespace VarPrice.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVarPriceApplication(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CrawlerOptions>(configuration.GetSection("Crawler"));
        services.Configure<QueueOptions>(configuration.GetSection("Queue"));
        services.AddScoped<ISitemapProductUrlDiscoverySource, SitemapProductUrlDiscoverySource>();
        services.AddScoped<IProductUrlDiscoveryService, ProductUrlDiscoveryService>();
        services.AddScoped<RunCrawlerUseCase>();
        services.AddScoped<IRunCrawlerUseCase>(provider => provider.GetRequiredService<RunCrawlerUseCase>());
        return services;
    }

    /// <summary>Loads URL exclusion filters from a JSON file defined in configuration.</summary>
    /// <param name="services">Service collection to register options in.</param>
    /// <param name="configuration">Configuration to read the file path from.</param>
    /// <param name="contentRootPath">Content root path used to resolve relative paths.</param>
    public static IServiceCollection AddUrlFilterOptionsFromFile(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        var pathSetting = configuration.GetSection("Crawler").GetValue<string>("UrlFilterFilePath");
        if (string.IsNullOrWhiteSpace(pathSetting))
        {
            throw new InvalidOperationException("Missing required configuration value: Crawler:UrlFilterFilePath");
        }

        var resolvedPath = Path.IsPathRooted(pathSetting)
            ? pathSetting
            : Path.GetFullPath(Path.Combine(contentRootPath, pathSetting));

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"URL filter file not found: {resolvedPath}", resolvedPath);
        }

        UrlFilterOptions options;
        try
        {
            var json = File.ReadAllText(resolvedPath);
            options = JsonSerializer.Deserialize<UrlFilterOptions>(
                          json,
                          new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? new UrlFilterOptions();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON in URL filter file: {resolvedPath}", ex);
        }

        options.ExcludedUrlSubstrings = options.ExcludedUrlSubstrings
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        services.AddSingleton<IOptions<UrlFilterOptions>>(Options.Create(options));
        return services;
    }

    /// <summary>Registers the category seed URL file location from crawler configuration.</summary>
    public static IServiceCollection AddCategorySeedUrlFileOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        var pathSetting = configuration.GetSection("Crawler").GetValue<string>("CategorySeedUrlsFilePath");
        var resolvedPath = string.IsNullOrWhiteSpace(pathSetting)
            ? string.Empty
            : ResolveConfiguredPath(pathSetting, contentRootPath);

        services.AddSingleton<IOptions<CategorySeedUrlFileOptions>>(Options.Create(new CategorySeedUrlFileOptions
        {
            PathSetting = pathSetting ?? string.Empty,
            ResolvedPath = resolvedPath
        }));
        return services;
    }

    private static string ResolveConfiguredPath(string pathSetting, string contentRootPath)
    {
        if (Path.IsPathRooted(pathSetting))
        {
            return pathSetting;
        }

        var contentRootPathCandidate = Path.GetFullPath(Path.Combine(contentRootPath, pathSetting));
        if (File.Exists(contentRootPathCandidate))
        {
            return contentRootPathCandidate;
        }

        var parentDirectory = Directory.GetParent(contentRootPath)?.FullName;
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            return contentRootPathCandidate;
        }

        var parentPathCandidate = Path.GetFullPath(Path.Combine(parentDirectory, pathSetting));
        return File.Exists(parentPathCandidate) ? parentPathCandidate : contentRootPathCandidate;
    }
}
