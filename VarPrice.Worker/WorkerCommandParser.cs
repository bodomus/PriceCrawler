namespace VarPrice.Worker;

public sealed record WorkerCommandParseResult(
    WorkerCommand? Command,
    bool ShowHelp,
    string? ErrorMessage)
{
    public bool IsValid => ErrorMessage is null;
}

public static class WorkerCommandParser
{
    public const int SuccessExitCode = 0;
    public const int FailedRunExitCode = 1;
    public const int InvalidCommandExitCode = 2;

    public static WorkerCommandParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Any(IsHelpArgument))
        {
            return new WorkerCommandParseResult(null, true, null);
        }

        var once = args.Any(arg => string.Equals(arg, "--once", StringComparison.OrdinalIgnoreCase));
        WorkerRunMode? selectedMode = null;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--once", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(arg, "--job", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Count)
                {
                    return Invalid("Missing value for --job.");
                }

                var job = args[++index];
                if (!TryParseMode(job, out var jobMode))
                {
                    return Invalid($"Unsupported job: {job}");
                }

                var conflict = TrySetMode(ref selectedMode, jobMode);
                if (conflict is not null)
                {
                    return Invalid(conflict);
                }

                continue;
            }

            if (string.Equals(arg, "--collect-prices", StringComparison.OrdinalIgnoreCase))
            {
                var conflict = TrySetMode(ref selectedMode, WorkerRunMode.CollectPrices);
                if (conflict is not null)
                {
                    return Invalid(conflict);
                }

                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                return Invalid($"Unsupported option: {arg}");
            }

            if (!TryParseMode(arg, out var commandMode))
            {
                return Invalid($"Unsupported command: {arg}");
            }

            var positionalConflict = TrySetMode(ref selectedMode, commandMode);
            if (positionalConflict is not null)
            {
                return Invalid(positionalConflict);
            }
        }

        return new WorkerCommandParseResult(
            new WorkerCommand(selectedMode ?? WorkerRunMode.Vegetables, once),
            false,
            null);
    }

    public static string GetHelpText() =>
        """
        VarPrice.Worker

        Usage:
          VarPrice.Worker vegetables [--once]
          VarPrice.Worker catalog-refresh
          VarPrice.Worker collect-prices

        Legacy aliases:
          VarPrice.Worker --job vegetables [--once]
          VarPrice.Worker --job catalog-refresh
          VarPrice.Worker --job collect-prices
          VarPrice.Worker --collect-prices

        Exit codes:
          0  run completed with status ok
          1  run completed with an error status or was cancelled
          2  unsupported command or option
        """;

    private static WorkerCommandParseResult Invalid(string message) =>
        new(null, false, message);

    private static bool IsHelpArgument(string arg) =>
        string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseMode(string value, out WorkerRunMode mode)
    {
        if (string.Equals(value, "vegetables", StringComparison.OrdinalIgnoreCase))
        {
            mode = WorkerRunMode.Vegetables;
            return true;
        }

        if (string.Equals(value, "catalog-refresh", StringComparison.OrdinalIgnoreCase))
        {
            mode = WorkerRunMode.CatalogRefresh;
            return true;
        }

        if (string.Equals(value, "collect-prices", StringComparison.OrdinalIgnoreCase))
        {
            mode = WorkerRunMode.CollectPrices;
            return true;
        }

        mode = WorkerRunMode.Vegetables;
        return false;
    }

    private static string? TrySetMode(ref WorkerRunMode? selectedMode, WorkerRunMode mode)
    {
        if (selectedMode is null)
        {
            selectedMode = mode;
            return null;
        }

        return selectedMode == mode
            ? null
            : $"Conflicting worker commands: {FormatMode(selectedMode.Value)} and {FormatMode(mode)}.";
    }

    private static string FormatMode(WorkerRunMode mode) =>
        mode switch
        {
            WorkerRunMode.Vegetables => "vegetables",
            WorkerRunMode.CatalogRefresh => "catalog-refresh",
            WorkerRunMode.CollectPrices => "collect-prices",
            _ => mode.ToString()
        };
}
