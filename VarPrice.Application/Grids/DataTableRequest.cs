namespace VarPrice.Application.Grids;

public sealed class DataTableRequest
{
    public int Draw { get; init; }

    public int Start { get; init; }

    public int Length { get; init; }

    public string? SearchValue { get; init; }

    public int OrderColumn { get; init; }

    public bool OrderAscending { get; init; }
}
