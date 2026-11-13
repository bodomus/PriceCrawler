namespace PriceCrawler.Api.Startup;

/// <summary>CORS for the browser frontend, which is served from another address.</summary>
internal static class CorsSetup
{
    /// <summary>Name of the policy applied to every endpoint.</summary>
    public const string PolicyName = "Frontend";

    /// <summary>Configuration key with the allowed frontend origins.</summary>
    public const string OriginsKey = "Cors:AllowedOrigins";

    /// <summary>
    /// Allows the origins listed in <c>Cors:AllowedOrigins</c> to call the API with any method and
    /// header. With no origins configured, cross-origin calls stay blocked.
    /// </summary>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration)
    {
        string[] origins = configuration.GetSection(OriginsKey).Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(PolicyName, policy =>
            policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader()));
        return services;
    }
}
