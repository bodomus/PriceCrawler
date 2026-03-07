using VarPrice.Application.Grids.Runs.Dto;

namespace VarPrice.Application.Grids.Runs;

public sealed class GetProductsGridQueryService(
    IProductsGridQuerySource querySource,
    IDataTableQueryService gridService) : IGetProductsGridQueryService
{
    public Task<DataTableResponse<ProductGridRowDto>> ExecuteAsync(
        long snapshotId,
        DataTableRequest request,
        CancellationToken ct = default)
    {
        var query = querySource.Build(snapshotId);

        return gridService.ExecuteAsync(
            query,
            request,
            row => new ProductGridRowDto
            {
                Id = row.ProductKey,
                Name = row.Name,
                Sku = row.ProductId,
                Url = row.Url,
                Price = row.SnapshotPrice,
                Unit = FormatUnit(row.PackValue, row.PackUnit),
                UpdatedAtUtc = row.CreatedAtUtc
            },
            (filteredQuery, search) =>
            {
                if (string.IsNullOrWhiteSpace(search))
                {
                    return filteredQuery;
                }

                var hasId = long.TryParse(search, out var parsedId);
                var normalizedSearch = search.ToLower();

                return filteredQuery.Where(row =>
                    (hasId && row.ProductKey == parsedId) ||
                    row.Name.ToLower().Contains(normalizedSearch) ||
                    row.ProductId.ToLower().Contains(normalizedSearch));
            },
            (orderedQuery, column, asc) => column switch
            {
                0 => asc ? orderedQuery.OrderBy(x => x.ProductKey) : orderedQuery.OrderByDescending(x => x.ProductKey),
                1 => asc ? orderedQuery.OrderBy(x => x.Name) : orderedQuery.OrderByDescending(x => x.Name),
                2 => asc ? orderedQuery.OrderBy(x => x.ProductId) : orderedQuery.OrderByDescending(x => x.ProductId),
                3 => asc ? orderedQuery.OrderBy(x => x.Url) : orderedQuery.OrderByDescending(x => x.Url),
                4 => asc
                    ? orderedQuery.OrderBy(x => x.SnapshotPrice)
                    : orderedQuery.OrderByDescending(x => x.SnapshotPrice),
                5 => asc ? orderedQuery.OrderBy(x => x.PackUnit) : orderedQuery.OrderByDescending(x => x.PackUnit),
                6 => asc
                    ? orderedQuery.OrderBy(x => x.CreatedAtUtc)
                    : orderedQuery.OrderByDescending(x => x.CreatedAtUtc),
                _ => orderedQuery.OrderBy(x => x.Name)
            },
            ct);
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
}
