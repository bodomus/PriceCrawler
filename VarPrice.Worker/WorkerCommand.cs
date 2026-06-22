namespace VarPrice.Worker;

public enum WorkerRunMode
{
    Vegetables,
    CatalogRefresh,
    CollectPrices
}

public sealed record WorkerCommand(WorkerRunMode Mode, bool Once);
