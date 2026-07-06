using PriceCrawler.Application.Models;
using PriceCrawler.Web.ViewModels.Shared;

namespace PriceCrawler.Web.ViewModels.Runs;

public sealed class RunsDashboardVm
{
    public string PageTitle { get; init; } = "VARUS - Dashboard";

    public string AppVersion { get; init; } = "unknown";

    public CrawlerRunResult? LatestRun { get; init; }

    public StatusBarViewModel? StatusBar { get; init; }
}
