using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

using VarPrice.Application.Grids.Runs;
using VarPrice.Application.Grids.Runs.Dto;
using VarPrice.Application.Grids.Runs.QueryRows;

namespace VarPrice.Infrastructure.Queries.Runs;

public sealed class ProductAnalysisService(
    IProductDetailsQuerySource productDetailsQuerySource,
    IProductPriceHistoryQuerySource productPriceHistoryQuerySource) : IProductAnalysisService
{
    public async Task<ProductAnalysisDto?> GetAsync(long snapshotId, CancellationToken ct)
    {
        var detailsRow = await ToFirstOrDefaultAsync(productDetailsQuerySource.Build(snapshotId), ct);
        if (detailsRow is null)
        {
            return null;
        }

        var historyRows = await ToListAsync(productPriceHistoryQuerySource.Build(snapshotId), ct);
        var history = historyRows
            .OrderByDescending(row => row.CapturedAtUtc)
            .Select(MapHistoryRow)
            .ToArray();

        return new ProductAnalysisDto
        {
            SnapshotId = snapshotId,
            ProductCard = MapDetailsRow(detailsRow),
            Analytics = BuildAnalytics(snapshotId, historyRows),
            History = history
        };
    }

    private static ProductDetailsDto MapDetailsRow(ProductDetailsQueryRow row)
    {
        return new ProductDetailsDto
        {
            Id = row.Id,
            SnapshotId = row.SnapshotId,
            RunId = row.RunId,
            Name = row.Name,
            Sku = row.ExternalId,
            Url = row.Url,
            Slug = row.Slug,
            Unit = FormatUnit(row.PackValue, row.PackUnit),
            CurrentPrice = row.CurrentPrice,
            OldPrice = row.OldPrice,
            DiscountPercent = row.DiscountPercent,
            PromoFlag = row.PromoFlag,
            InStock = row.InStock,
            UpdatedAtUtc = row.UpdatedAtUtc,
            CapturedAtUtc = row.CapturedAtUtc,
            Source = row.Source,
            Brand = row.Brand,
            Category = row.Category,
            ImageUrl = row.ImageUrl
        };
    }

    private static ProductPriceHistoryRowDto MapHistoryRow(ProductPriceHistoryQueryRow row)
    {
        return new ProductPriceHistoryRowDto
        {
            Id = row.Id,
            RunId = row.RunId,
            CapturedAtUtc = row.CapturedAtUtc,
            Price = row.Price,
            OldPrice = row.OldPrice,
            DiscountPercent = row.DiscountPercent,
            PromoFlag = row.PromoFlag,
            InStock = row.InStock,
            Source = row.Source
        };
    }

    private static string? FormatUnit(decimal? packValue, string? packUnit)
    {
        if (packValue is null && string.IsNullOrWhiteSpace(packUnit))
        {
            return null;
        }

        if (packValue is null)
        {
            return packUnit;
        }

        return string.IsNullOrWhiteSpace(packUnit)
            ? packValue.Value.ToString("0.###")
            : $"{packValue.Value:0.###} {packUnit}";
    }

    private static ProductAnalyticsDto BuildAnalytics(long snapshotId,
        IReadOnlyList<ProductPriceHistoryQueryRow> historyRows)
    {
        var orderedRows = historyRows
            .OrderBy(row => row.CapturedAtUtc)
            .ToList();

        var priceRows = orderedRows
            .Where(row => row.Price is not null)
            .ToList();

        var selectedRow = orderedRows.LastOrDefault(row => row.Id == snapshotId);

        ProductPriceHistoryQueryRow? previousPriceRow = null;
        var selectedRowIndex = selectedRow is null
            ? -1
            : orderedRows.FindLastIndex(row => row.Id == selectedRow.Id);

        if (selectedRowIndex > 0)
        {
            for (var index = selectedRowIndex - 1; index >= 0; index--)
            {
                var candidate = orderedRows[index];
                if (candidate.Price is not null)
                {
                    previousPriceRow = candidate;
                    break;
                }
            }
        }

        var firstPriceRow = priceRows.FirstOrDefault();
        var latestPriceRow = priceRows.LastOrDefault();
        var minPrice = priceRows.Count > 0 ? priceRows.Min(row => row.Price) : null;
        var maxPrice = priceRows.Count > 0 ? priceRows.Max(row => row.Price) : null;
        decimal? averagePrice = priceRows.Count > 0
            ? decimal.Round(priceRows.Average(row => row.Price!.Value), 2, MidpointRounding.AwayFromZero)
            : null;

        var points = orderedRows
            .Select(row => new ProductAnalyticsPointDto
            {
                SnapshotId = row.Id,
                RunId = row.RunId,
                CapturedAtUtc = row.CapturedAtUtc,
                Price = row.Price,
                OldPrice = row.OldPrice,
                DiscountPercent = row.DiscountPercent,
                PromoFlag = row.PromoFlag,
                InStock = row.InStock,
                Source = row.Source
            })
            .ToArray();

        var selectedPrice = selectedRow?.Price;
        var previousPrice = previousPriceRow?.Price;
        var firstObservedPrice = firstPriceRow?.Price;

        return new ProductAnalyticsDto
        {
            SnapshotId = snapshotId,
            HistoryPointsCount = orderedRows.Count,
            PricePointsCount = priceRows.Count,
            PromoMomentsCount = orderedRows.Count(row => row.PromoFlag),
            InStockMomentsCount = orderedRows.Count(row => row.InStock),
            SelectedCapturedAtUtc = selectedRow?.CapturedAtUtc,
            FirstCapturedAtUtc = orderedRows.FirstOrDefault()?.CapturedAtUtc,
            LastCapturedAtUtc = orderedRows.LastOrDefault()?.CapturedAtUtc,
            SelectedPrice = selectedPrice,
            PreviousPrice = previousPrice,
            FirstObservedPrice = firstObservedPrice,
            LatestObservedPrice = latestPriceRow?.Price,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            AveragePrice = averagePrice,
            PriceSpread = minPrice is not null && maxPrice is not null ? maxPrice.Value - minPrice.Value : null,
            ChangeFromPreviousAmount = selectedPrice is not null && previousPrice is not null
                ? selectedPrice.Value - previousPrice.Value
                : null,
            ChangeFromPreviousPercent = CalculatePriceChangePercent(previousPrice, selectedPrice),
            ChangeFromFirstAmount = selectedPrice is not null && firstObservedPrice is not null
                ? selectedPrice.Value - firstObservedPrice.Value
                : null,
            ChangeFromFirstPercent = CalculatePriceChangePercent(firstObservedPrice, selectedPrice),
            Points = points
        };
    }

    private static decimal? CalculatePriceChangePercent(decimal? baselinePrice, decimal? targetPrice)
    {
        if (baselinePrice is null || targetPrice is null || baselinePrice <= 0)
        {
            return null;
        }

        var change = ((targetPrice.Value - baselinePrice.Value) / baselinePrice.Value) * 100m;
        return decimal.Round(change, 1, MidpointRounding.AwayFromZero);
    }

    private static Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken ct)
    {
        if (query.Provider is IAsyncQueryProvider)
        {
            return EntityFrameworkQueryableExtensions.ToListAsync(query, ct);
        }

        return Task.FromResult(query.ToList());
    }

    private static Task<T?> ToFirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken ct)
    {
        if (query.Provider is IAsyncQueryProvider)
        {
            return EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(query, ct);
        }

        return Task.FromResult(query.FirstOrDefault());
    }
}
