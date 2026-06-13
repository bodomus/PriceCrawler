using VarPrice.Application.Models;

namespace VarPrice.Application.UseCases;

public sealed record CatalogRefreshSafetyInput(
    string DiscoverySource,
    int AcceptedCount,
    int ActiveCountBefore,
    CrawlerOptions Options);

public sealed record CatalogRefreshSafetyResult(
    bool IsError,
    bool CanDeactivate,
    string? Reason,
    string? ErrorCode);

public static class CatalogRefreshSafetyPolicy
{
    public const string DeactivationDisabled = "deactivation_disabled";
    public const string ScopedFilterActive = "scoped_filter_active";
    public const string UnsupportedDiscoveryMode = "unsupported_discovery_mode";
    public const string BelowMinimum = "catalog_refresh_below_minimum";
    public const string RatioTooLow = "catalog_refresh_ratio_too_low";

    public static CatalogRefreshSafetyResult Evaluate(CatalogRefreshSafetyInput input)
    {
        if (!input.Options.CatalogDeactivationEnabled)
        {
            return Skip(DeactivationDisabled);
        }

        if (!string.IsNullOrWhiteSpace(input.Options.VegetablesUrlContains))
        {
            return Skip(ScopedFilterActive);
        }

        if (!IsFullCatalogDiscovery(input.DiscoverySource, input.Options))
        {
            return Skip(UnsupportedDiscoveryMode);
        }

        var minimumExpected = NormalizeMinimumExpected(input.Options.CatalogMinimumExpectedUrls);
        if (input.AcceptedCount < minimumExpected)
        {
            return Error(BelowMinimum);
        }

        var ratio = NormalizePreviousRatio(input.Options.CatalogMinimumPreviousRatio);
        if (input.ActiveCountBefore > 0)
        {
            var required = (int)Math.Ceiling(input.ActiveCountBefore * ratio);
            if (input.AcceptedCount < required)
            {
                return Error(RatioTooLow);
            }
        }

        return new CatalogRefreshSafetyResult(false, true, null, null);
    }

    public static bool IsFullCatalogDiscovery(string discoverySource, CrawlerOptions options) =>
        string.Equals(discoverySource, "category-seed", StringComparison.Ordinal)
        && string.IsNullOrWhiteSpace(options.VegetablesUrlContains);

    public static int NormalizeGracePeriodDays(int value) => Math.Max(1, value);

    public static int NormalizeMinimumExpected(int value) => Math.Max(1, value);

    public static double NormalizePreviousRatio(double value) => value > 0.0d && value <= 1.0d ? value : 0.5d;

    private static CatalogRefreshSafetyResult Skip(string reason) => new(false, false, reason, null);

    private static CatalogRefreshSafetyResult Error(string errorCode) => new(true, false, errorCode, errorCode);
}
