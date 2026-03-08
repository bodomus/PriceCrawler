namespace VarPrice.Application.Models;

public sealed class QueueOptions
{
    public int BatchSize { get; set; } = 50;
    public int PollDelayMs { get; set; } = 300;
    public int LeaseSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 500;
    public int RetryMaxDelayMs { get; set; } = 30_000;
    public int ReaperIntervalSeconds { get; set; } = 15;
}
