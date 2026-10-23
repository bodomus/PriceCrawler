using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Hosting;

namespace PriceCrawler.Worker;

/// <summary>
/// Adds the optional, git-ignored <c>appsettings.Local.json</c> with personal debug overrides.
/// The file is loaded only in the Development environment, after
/// <c>appsettings.Development.json</c> and before unprefixed environment variables and
/// command-line arguments, so explicit environment overrides still win.
/// Stage, Staging, Production and Test never read it.
/// </summary>
internal static class LocalDeveloperSettings
{
    public const string FileName = "appsettings.Local.json";

    public static void Add(ConfigurationManager configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        configuration.AddJsonFile(
            Path.Combine(environment.ContentRootPath, FileName),
            optional: true,
            reloadOnChange: false);

        IList<IConfigurationSource> sources = configuration.Sources;
        var localSource = sources[sources.Count - 1];
        for (var index = sources.Count - 2; index >= 0; index--)
        {
            if (sources[index] is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
            {
                sources.RemoveAt(sources.Count - 1);
                sources.Insert(index, localSource);
                return;
            }
        }
    }
}
