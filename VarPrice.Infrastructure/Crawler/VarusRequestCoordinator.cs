using System.Diagnostics;
using System.Threading.RateLimiting;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VarPrice.Application.Models;

namespace VarPrice.Infrastructure.Crawler;

public sealed class VarusRequestCoordinator : IAsyncDisposable
{
    private const int PermitsPerRequest = 10;
    private readonly TokenBucketRateLimiter _limiter;
    private readonly ILogger<VarusRequestCoordinator> _logger;
    private readonly int _jitterMinMs;
    private readonly int _jitterMaxMs;
    private readonly int _breakerFailureThreshold;
    private readonly int _breakerOpenSeconds;
    private readonly long _startedAtTimestamp = Stopwatch.GetTimestamp();

    private long _acquiredRequests;
    private int _consecutiveTemporaryFailures;
    private long _breakerOpenUntilUtcTicks;

    public VarusRequestCoordinator(IOptions<CrawlerOptions> options, ILogger<VarusRequestCoordinator> logger)
    {
        _logger = logger;

        var o = options.Value;
        var requestsPerSecond = Math.Max(0.1d, o.RequestsPerSecond);
        var permitsPerSecond = Math.Max(PermitsPerRequest, (int)Math.Ceiling(requestsPerSecond * PermitsPerRequest));

        _jitterMinMs = Math.Max(0, o.JitterDelayMsMin);
        _jitterMaxMs = Math.Max(_jitterMinMs, o.JitterDelayMsMax);
        _breakerFailureThreshold = Math.Max(1, o.BreakerFailureThreshold);
        _breakerOpenSeconds = Math.Max(1, o.BreakerOpenSeconds);

        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = permitsPerSecond,
            TokensPerPeriod = permitsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = Math.Max(permitsPerSecond * Math.Max(1, o.MaxConcurrency), permitsPerSecond * 4),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    }

    public async Task AcquireRequestSlotAsync(CancellationToken ct)
    {
        await WaitIfBreakerOpenAsync(ct);

        using var lease = await _limiter.AcquireAsync(PermitsPerRequest, ct);
        if (!lease.IsAcquired)
        {
            throw new InvalidOperationException("Rate limiter lease was not acquired for VARUS request.");
        }

        if (_jitterMaxMs > 0)
        {
            var jitter = Random.Shared.Next(_jitterMinMs, _jitterMaxMs + 1);
            if (jitter > 0)
            {
                await Task.Delay(jitter, ct);
            }
        }

        Interlocked.Increment(ref _acquiredRequests);
    }

    public double GetApproximateRps()
    {
        var seconds = Stopwatch.GetElapsedTime(_startedAtTimestamp).TotalSeconds;
        if (seconds <= 0d)
        {
            return 0d;
        }

        var requests = Interlocked.Read(ref _acquiredRequests);
        return requests / seconds;
    }

    public void MarkTemporaryFailure()
    {
        var failures = Interlocked.Increment(ref _consecutiveTemporaryFailures);
        if (failures < _breakerFailureThreshold)
        {
            return;
        }

        var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
        var openUntilTicks = DateTimeOffset.UtcNow.AddSeconds(_breakerOpenSeconds).UtcTicks;

        while (true)
        {
            var currentOpenUntilTicks = Volatile.Read(ref _breakerOpenUntilUtcTicks);
            if (currentOpenUntilTicks > nowTicks)
            {
                return;
            }

            var exchanged =
                Interlocked.CompareExchange(ref _breakerOpenUntilUtcTicks, openUntilTicks, currentOpenUntilTicks);
            if (exchanged == currentOpenUntilTicks)
            {
                Interlocked.Exchange(ref _consecutiveTemporaryFailures, 0);
                _logger.LogWarning(
                    "VARUS breaker opened for {OpenSeconds}s after {Failures} consecutive temporary errors",
                    _breakerOpenSeconds,
                    failures);
                return;
            }
        }
    }

    public void ResetTemporaryFailureStreak()
        => Interlocked.Exchange(ref _consecutiveTemporaryFailures, 0);

    public async Task WaitIfBreakerOpenAsync(CancellationToken ct)
    {
        while (true)
        {
            var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
            var openUntilTicks = Volatile.Read(ref _breakerOpenUntilUtcTicks);
            if (openUntilTicks <= nowTicks)
            {
                return;
            }

            var remaining = TimeSpan.FromTicks(openUntilTicks - nowTicks);
            var delay = remaining > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining;
            if (delay <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(delay, ct);
        }
    }

    public ValueTask DisposeAsync() => _limiter.DisposeAsync();
}
