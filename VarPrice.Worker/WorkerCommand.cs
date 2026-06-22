namespace VarPrice.Worker;

public enum WorkerRunMode
{
    Vegetables,
    CatalogRefresh,
    CollectPrices,
    RunAll
}

public sealed record WorkerCommand(WorkerRunMode Mode, bool Once);
