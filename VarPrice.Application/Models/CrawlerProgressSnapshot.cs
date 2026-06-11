namespace VarPrice.Application.Models;

public sealed record CrawlerProgressSnapshot(
    int TotalDiscovered,
    int NewProducts,
    int UpdatedProducts,
    int SelectedForCheck,
    int CheckedProducts,
    int SuccessfulProducts,
    int FailedProducts,
    string CurrentStage,
    string CurrentItem);
