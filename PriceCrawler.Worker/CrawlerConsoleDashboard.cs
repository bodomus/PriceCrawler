using PriceCrawler.Application.Models;

namespace PriceCrawler.Worker;

internal sealed class CrawlerConsoleDashboard(CrawlerProgressState state, TimeSpan refreshInterval)
{
    private const int PanelHeight = 17;
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _cts;
    private Task? _renderLoop;
    private TextWriter? _originalOut;
    private bool _started;
    private bool _enabled;

    public bool IsEnabled => _enabled;

    public static string GetDisabledReason()
    {
        if (Console.IsOutputRedirected)
        {
            return "output redirected";
        }

        var height = GetConsoleHeight();
        return height <= PanelHeight + 2
            ? $"console height too small ({height})"
            : "dashboard disabled";
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _enabled = CanUseDashboard();
        if (!_enabled)
        {
            return;
        }

        _originalOut = Console.Out;
        Console.SetOut(new ConsoleDashboardTextWriter(_originalOut, _syncRoot));
        _cts = new CancellationTokenSource();

        lock (_syncRoot)
        {
            WriteAnsi("\u001b[?25l");
            ConfigureScrollRegion();
            RenderCore();
            WriteAnsi($"\u001b[{PanelHeight + 1};1H");
        }

        _renderLoop = Task.Run(() => RenderUntilStoppedAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (!_started || !_enabled)
        {
            return;
        }

        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_renderLoop is not null)
        {
            try
            {
                await _renderLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        lock (_syncRoot)
        {
            RenderCore();
            ResetScrollRegion();
            WriteAnsi($"\u001b[{PanelHeight + 1};1H");
            WriteAnsi("\u001b[?25h");
            _originalOut?.Flush();
        }

        if (_originalOut is not null)
        {
            Console.SetOut(_originalOut);
        }

        _cts?.Dispose();
    }

    private async Task RenderUntilStoppedAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(refreshInterval, ct);
            lock (_syncRoot)
            {
                RenderCore();
            }
        }
    }

    private void RenderCore()
    {
        var snapshot = state.GetSnapshot();
        var width = GetConsoleWidth();
        var total = snapshot.TotalDiscovered;
        var selected = snapshot.SelectedForCheck;
        var percent = snapshot.DiscoveryTotalSeeds > 0 && selected == 0
            ? CrawlerProgressFormatter.CalculatePercent(snapshot.DiscoveryProcessedSeeds, snapshot.DiscoveryTotalSeeds)
            : CrawlerProgressFormatter.CalculatePercent(snapshot.ProductProcessed, snapshot.ProductQueueTotal);

        WriteAnsi("\u001b7");
        WriteLine(1, FormatLine("Обнаружено товаров", CrawlerProgressFormatter.FormatNumber(total), width, "\u001b[36m"));
        WriteLine(2,
            FormatLine("Новых товаров", CrawlerProgressFormatter.FormatCurrentOverTotal(snapshot.NewProducts, total), width,
                "\u001b[37m"));
        WriteLine(3,
            FormatLine("Обновлено товаров", CrawlerProgressFormatter.FormatCurrentOverTotal(snapshot.UpdatedProducts, total),
                width, "\u001b[37m"));
        WriteLine(4,
            FormatLine("Выбрано из каталога", CrawlerProgressFormatter.FormatCurrentOverTotal(selected, total), width,
                "\u001b[36m"));
        WriteLine(5,
            FormatLine("Listing в очереди", CrawlerProgressFormatter.FormatNumber(snapshot.ListingQueueTotal), width,
                "\u001b[33m"));
        WriteLine(6,
            FormatLine("Listing обработано",
                CrawlerProgressFormatter.FormatCurrentOverTotal(snapshot.ListingProcessed, snapshot.ListingQueueTotal),
                width, "\u001b[33m"));
        WriteLine(7,
            FormatLine("Listing успешно", CrawlerProgressFormatter.FormatNumber(snapshot.ListingSucceeded), width,
                "\u001b[32m"));
        WriteLine(8,
            FormatLine("Listing ошибок", CrawlerProgressFormatter.FormatNumber(snapshot.ListingFailed), width,
                "\u001b[31m"));
        WriteLine(9,
            FormatLine("Product URL найдено",
                CrawlerProgressFormatter.FormatNumber(snapshot.ProductLinksDiscoveredFromListings), width,
                "\u001b[36m"));
        WriteLine(10,
            FormatLine("Product URL добавлено",
                CrawlerProgressFormatter.FormatNumber(snapshot.ProductLinksEnqueuedFromListings), width,
                "\u001b[36m"));
        WriteLine(11,
            FormatLine("Товаров в очереди", CrawlerProgressFormatter.FormatNumber(snapshot.ProductQueueTotal), width,
                "\u001b[36m"));
        WriteLine(12,
            FormatLine("Товаров обработано",
                CrawlerProgressFormatter.FormatCurrentOverTotal(snapshot.ProductProcessed, snapshot.ProductQueueTotal),
                width, "\u001b[33m"));
        WriteLine(13,
            FormatLine("Товаров успешно",
                CrawlerProgressFormatter.FormatCurrentOverTotal(snapshot.ProductSucceeded, snapshot.ProductQueueTotal),
                width, "\u001b[32m"));
        WriteLine(14,
            FormatLine("Ошибок товаров",
                CrawlerProgressFormatter.FormatCurrentOverTotal(snapshot.ProductFailed, snapshot.ProductQueueTotal),
                width, "\u001b[31m"));
        WriteLine(15,
            FormatLine("Текущий этап", string.IsNullOrWhiteSpace(snapshot.CurrentStage) ? "-" : snapshot.CurrentStage,
                width, "\u001b[33m"));
        WriteLine(16,
            FormatLine("Текущая ссылка", string.IsNullOrWhiteSpace(snapshot.CurrentItem) ? "-" : snapshot.CurrentItem,
                width, "\u001b[37m"));
        WriteLine(17,
            FormatLine("Выполнение товаров", CrawlerProgressFormatter.FormatPercent(percent), width, "\u001b[36m"));
        WriteAnsi("\u001b8");
        _originalOut?.Flush();
    }

    private static string FormatLine(string label, string value, int width, string valueColor)
    {
        const string labelColor = "\u001b[90m";
        const string resetColor = "\u001b[0m";
        var availableValueWidth = Math.Max(1, width - label.Length - 3);
        var trimmedValue = TrimToWidth(value, availableValueWidth);
        return $"{labelColor}{label}: {valueColor}{trimmedValue}{resetColor}";
    }

    private static string TrimToWidth(string value, int maxWidth)
    {
        if (value.Length <= maxWidth)
        {
            return value;
        }

        return maxWidth <= 1 ? value[..1] : $"{value[..(maxWidth - 1)]}...";
    }

    private void WriteLine(int lineNumber, string text)
    {
        WriteAnsi($"\u001b[{lineNumber};1H\u001b[2K{text}");
    }

    private static int GetConsoleWidth()
    {
        try
        {
            return Math.Max(20, Console.WindowWidth);
        }
        catch (IOException)
        {
            return 80;
        }
        catch (InvalidOperationException)
        {
            return 80;
        }
    }

    private static int GetConsoleHeight()
    {
        try
        {
            return Console.WindowHeight;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static bool CanUseDashboard() => string.Equals(
        GetDisabledReason(),
        "dashboard disabled",
        StringComparison.Ordinal);

    private void ConfigureScrollRegion()
    {
        var height = GetConsoleHeight();
        if (height <= PanelHeight + 2)
        {
            return;
        }

        WriteAnsi($"\u001b[{PanelHeight + 1};{height}r");
    }

    private void ResetScrollRegion() => WriteAnsi("\u001b[r");

    private void WriteAnsi(string value) => _originalOut?.Write(value);
}
