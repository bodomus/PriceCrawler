using Microsoft.Extensions.Logging;

using VarPrice.Application.Abstractions;
using VarPrice.Application.Models;

namespace VarPrice.Infrastructure.Crawler;

public sealed class StubProductCardExtractor(
    ILogger<StubProductCardExtractor> logger) : IProductCardExtractor
{
    public Task<ProductExtractResult> ExtractAsync(string url, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var slug = TryBuildSlug(url);
        var card = new ProductCard(
            ExternalId: $"stub:{slug ?? StableUrlKey(url)}",
            Name: $"Stub product {slug ?? url}",
            Url: url,
            Slug: slug,
            Price: null,
            OldPrice: null,
            PromoFlag: false,
            InStock: true,
            PackValue: null,
            PackUnit: null);

        logger.LogInformation(
            "Product card extraction stubbed. Url={Url}; ExternalId={ExternalId}",
            url,
            card.ExternalId);

        return Task.FromResult(ProductExtractResult.Success(
            card,
            httpStatus: null,
            latencyMs: 0,
            approximateRps: 0d));
    }

    private static string? TryBuildSlug(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Segments
            .Select(segment => segment.Trim('/'))
            .LastOrDefault(segment => !string.IsNullOrWhiteSpace(segment));
    }

    private static string StableUrlKey(string url)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in url.ToUpperInvariant())
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return hash.ToString("x8");
        }
    }
}
