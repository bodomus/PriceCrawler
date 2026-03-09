using VarPrice.Application.Models;
using VarPrice.Web.ViewModels.Shared;

namespace VarPrice.Web.ViewModels.Runs;

public sealed class RunsDashboardVm
{
    public string PageTitle { get; init; } = "VARUS - Dashboard";

    public string AppVersion { get; init; } = "unknown";

    public CrawlerRunResult? LatestRun { get; init; }

    public StatusBarViewModel? StatusBar { get; init; }
}
