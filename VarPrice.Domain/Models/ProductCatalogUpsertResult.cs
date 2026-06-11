namespace VarPrice.Domain.Models;

public sealed record ProductCatalogUpsertResult(
    int ReceivedCount,
    int InsertedCount,
    int UpdatedCount);
