namespace VarPrice.Application.Grids;

public sealed class DataTableResponse<T>
{
    public int Draw { get; init; }

    public int RecordsTotal { get; init; }

    public int RecordsFiltered { get; init; }

    public IReadOnlyList<T> Data { get; init; } = [];
}
