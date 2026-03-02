namespace VarPrice.Application.Models;

/// <summary>Options that control exclusion of sitemap URLs.</summary>
public sealed class UrlFilterOptions
{
    /// <summary>Substrings that exclude URLs when matched (case-insensitive).</summary>
    public string[] ExcludedUrlSubstrings { get; set; } = Array.Empty<string>();
}
