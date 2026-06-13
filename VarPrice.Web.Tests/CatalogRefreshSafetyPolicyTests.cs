using VarPrice.Application.Models;
using VarPrice.Application.UseCases;

namespace VarPrice.Web.Tests;

public sealed class CatalogRefreshSafetyPolicyTests
{
    [Fact]
    public void CatalogRefreshSafetyPolicy_ValidFullRefresh_AllowsDeactivation()
    {
        var result = Evaluate(accepted: 100, activeBefore: 100);

        Assert.True(result.CanDeactivate);
        Assert.False(result.IsError);
    }

    [Fact]
    public void CatalogRefreshSafetyPolicy_DeactivationDisabled_SkipsDeactivation()
    {
        var result = Evaluate(accepted: 100, activeBefore: 100, options: new CrawlerOptions
        {
            CatalogDeactivationEnabled = false
        });

        Assert.False(result.CanDeactivate);
        Assert.Equal("deactivation_disabled", result.Reason);
    }

    [Fact]
    public void CatalogRefreshSafetyPolicy_VegetablesFilterActive_SkipsDeactivation()
    {
        var result = Evaluate(accepted: 100, activeBefore: 100, options: new CrawlerOptions
        {
            VegetablesUrlContains = "/ovochi"
        });

        Assert.False(result.CanDeactivate);
        Assert.Equal("scoped_filter_active", result.Reason);
    }

    [Fact]
    public void CatalogRefreshSafetyPolicy_AcceptedBelowMinimum_RejectsRefresh()
    {
        var result = Evaluate(accepted: 9, activeBefore: 0, options: new CrawlerOptions
        {
            CatalogMinimumExpectedUrls = 10
        });

        Assert.True(result.IsError);
        Assert.Equal("catalog_refresh_below_minimum", result.ErrorCode);
    }

    [Fact]
    public void CatalogRefreshSafetyPolicy_AcceptedBelowPreviousRatio_RejectsRefresh()
    {
        var result = Evaluate(accepted: 49, activeBefore: 100);

        Assert.True(result.IsError);
        Assert.Equal("catalog_refresh_ratio_too_low", result.ErrorCode);
    }

    [Fact]
    public void CatalogRefreshSafetyPolicy_NoPreviousCatalog_DoesNotApplyRatioCheck()
    {
        var result = Evaluate(accepted: 1, activeBefore: 0);

        Assert.True(result.CanDeactivate);
    }

    [Fact]
    public void CatalogRefreshSafetyPolicy_NonFullDiscoveryMode_SkipsDeactivation()
    {
        var result = CatalogRefreshSafetyPolicy.Evaluate(new CatalogRefreshSafetyInput(
            "sitemap",
            100,
            100,
            DefaultOptions()));

        Assert.False(result.CanDeactivate);
        Assert.Equal("unsupported_discovery_mode", result.Reason);
    }

    private static CatalogRefreshSafetyResult Evaluate(
        int accepted,
        int activeBefore,
        CrawlerOptions? options = null) =>
        CatalogRefreshSafetyPolicy.Evaluate(new CatalogRefreshSafetyInput(
            "category-seed",
            accepted,
            activeBefore,
            options ?? DefaultOptions()));

    private static CrawlerOptions DefaultOptions() => new()
    {
        CatalogMinimumExpectedUrls = 1,
        CatalogMinimumPreviousRatio = 0.5d
    };
}
