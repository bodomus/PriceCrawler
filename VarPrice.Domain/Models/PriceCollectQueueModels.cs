using VarPrice.Domain.Enums;

namespace VarPrice.Domain.Models;

public sealed record QueueEnqueueItem(
    string Url,
    string IdempotencyKey,
    long? ProductCatalogId = null,
    QueueItemKind PageKind = QueueItemKind.ProductPage);

public sealed record ReservedQueueItem(
    long Id,
    string Url,
    int Attempt,
    int MaxAttempts,
    string IdempotencyKey,
    long? ProductCatalogId = null,
    QueueItemKind PageKind = QueueItemKind.ProductPage);

public sealed record QueueRunStats(
    int Pending,
    int Reserved,
    int Retry,
    int Succeeded,
    int Dead);
