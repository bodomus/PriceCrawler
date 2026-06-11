using VarPrice.Application.Models;

namespace VarPrice.Worker;

internal sealed class CrawlerConsoleDashboard(CrawlerProgressState state, TimeSpan refreshInterval)
{
    private const int PanelHeight = 11;
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _cts;
    private Task? _renderLoop;
    private TextWriter? _originalOut;
    private bool _started;
    private bool _enabled;

    public bool IsEnabled => _enabled;

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
        var percent = CrawlerProgressFormatter.CalculatePercent(snapshot.CheckedProducts, selected);

        WriteAnsi("\u001b7");
        WriteLine(1, FormatLine("Обнаружено", CrawlerProgressFormatter.FormatNumber(total), width, "\u001b[36m"));
        WriteLine(2,
            FormatLine("Новых", CrawlerProgressFormatter.FormatCurrentOverTotal(snapshot.NewProducts, total), width,
                "\u001b[37m"));
        WriteLine(3,
            FormatLine("Обновлено", CrawlerProgressFormatter.FormatCurrentOverTotal(snapshot.UpdatedProducts, total),
                width, "\u001b[37m"));
        WriteLine(4,
            FormatLine("Выбрано на проверку", CrawlerProgressFormatter.FormatCurrentOverTotal(selected, total), width,
                "\u001b[36m"));
        WriteLine(5,
            FormatLine("Проверено", CrawlerProgressFormatter.FormatCurrentOverTotal(snapshot.CheckedProducts, selected),
                width, "\u001b[33m"));
        WriteLine(6,
            FormatLine("Успешно",
                CrawlerProgressFormatter.FormatCurrentOverTotal(snapshot.SuccessfulProducts, selected), width,
                "\u001b[32m"));
        WriteLine(7,
            FormatLine("Ошибок", CrawlerProgressFormatter.FormatCurrentOverTotal(snapshot.FailedProducts, selected),
                width, "\u001b[31m"));
        WriteLine(8,
            FormatLine("Текущий этап", string.IsNullOrWhiteSpace(snapshot.CurrentStage) ? "-" : snapshot.CurrentStage,
                width, "\u001b[33m"));
        WriteLine(9,
            FormatLine("Текущий товар", string.IsNullOrWhiteSpace(snapshot.CurrentItem) ? "-" : snapshot.CurrentItem,
                width, "\u001b[37m"));
        WriteLine(10, FormatLine("Выполнение", CrawlerProgressFormatter.FormatPercent(percent), width, "\u001b[36m"));
        WriteLine(11, new string('-', Math.Max(1, width - 1)));
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

        return maxWidth <= 1 ? value[..1] : $"{value[..(maxWidth - 1)]}…";
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

    private static bool CanUseDashboard() =>
        !Console.IsOutputRedirected && GetConsoleHeight() > PanelHeight + 2;

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
