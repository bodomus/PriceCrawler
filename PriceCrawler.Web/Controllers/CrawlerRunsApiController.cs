using Microsoft.AspNetCore.Mvc;

using PriceCrawler.Domain.Constants;
using PriceCrawler.Domain.Interfaces;

namespace PriceCrawler.Web.Controllers;

[ApiController]
[Route("api/crawler-runs")]
public sealed class CrawlerRunsApiController(ICrawlerRunReadRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetRecent(
        [FromQuery] int limit = 50,
        [FromQuery] string? runType = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        if (limit is < 1 or > 200) return BadRequest(new { error = "limit must be between 1 and 200." });
        if (!TryNormalize(runType, CrawlerRunTypes.IsSupported, out var normalizedRunType))
            return BadRequest(new { error = "runType must be catalog-refresh, price-collection, or legacy." });
        if (!TryNormalize(status, CrawlerRunStatuses.IsSupported, out var normalizedStatus))
            return BadRequest(new { error = "status must be running, ok, or error." });
        return Ok(await repository.GetRecentAsync(limit, normalizedRunType, normalizedStatus, ct));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct = default)
    {
        if (id <= 0) return BadRequest(new { error = "id must be positive." });
        var run = await repository.GetByIdAsync(id, ct);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? runType = null,
        CancellationToken ct = default)
    {
        var toUtc = to?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        var fromUtc = from?.ToUniversalTime() ?? toUtc.AddDays(-30);
        if (fromUtc >= toUtc) return BadRequest(new { error = "from must be earlier than to." });
        if (toUtc - fromUtc > TimeSpan.FromDays(365))
            return BadRequest(new { error = "date range cannot exceed 365 days." });
        if (!TryNormalize(runType, CrawlerRunTypes.IsSupported, out var normalizedRunType))
            return BadRequest(new { error = "runType must be catalog-refresh, price-collection, or legacy." });
        return Ok(await repository.GetAggregateAsync(fromUtc, toUtc, normalizedRunType, ct));
    }

    private static bool TryNormalize(string? value, Func<string, bool> isSupported, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = null;
            return true;
        }

        normalized = value.Trim().ToLowerInvariant();
        return isSupported(normalized);
    }
}
