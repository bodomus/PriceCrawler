namespace VarPrice.Domain.Constants;

public static class QueueItemStatuses
{
    public const string Pending = "pending";
    public const string Reserved = "reserved";
    public const string Succeeded = "succeeded";
    public const string Retry = "retry";
    public const string Dead = "dead";
}
