using System.Reflection;

namespace PriceCrawler.Api.Startup;

/// <summary>Detects that the process only generates the OpenAPI document at build time.</summary>
internal static class BuildTimeDocument
{
    /// <summary>
    /// <see langword="true"/> when the entry assembly is <c>GetDocument.Insider</c>, the tool of
    /// <c>Microsoft.Extensions.ApiDescription.Server</c> that runs the app to write the document.
    /// </summary>
    public static bool IsGenerating { get; } =
        string.Equals(Assembly.GetEntryAssembly()?.GetName().Name, "GetDocument.Insider", StringComparison.Ordinal);
}
