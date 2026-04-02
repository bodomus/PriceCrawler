namespace VarPrice.Infrastructure.Persistence;

public sealed class DbRoutineCall
{
    private readonly List<DbRoutineParameter> _parameters = [];

    private DbRoutineCall(string routineName, DbRoutineCallMode mode)
    {
        if (string.IsNullOrWhiteSpace(routineName))
        {
            throw new ArgumentException("Routine name is required.", nameof(routineName));
        }

        RoutineName = routineName.Trim();
        Mode = mode;
    }

    public string RoutineName { get; }
    public DbRoutineCallMode Mode { get; }
    public IReadOnlyList<DbRoutineParameter> Parameters => _parameters;

    public static DbRoutineCall ScalarFunction(string routineName)
        => new(routineName, DbRoutineCallMode.ScalarFunction);

    public static DbRoutineCall SetReturningFunction(string routineName)
        => new(routineName, DbRoutineCallMode.SetReturningFunction);

    public static DbRoutineCall Procedure(string routineName)
        => new(routineName, DbRoutineCallMode.Procedure);

    public DbRoutineCall AddParameter(string name, object? value)
    {
        var routineParameterName = NormalizeRoutineParameterName(name);
        if (_parameters.Any(x => string.Equals(x.Name, routineParameterName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Routine parameter '{routineParameterName}' is already defined for '{RoutineName}'.");
        }

        _parameters.Add(new DbRoutineParameter(routineParameterName, value));
        return this;
    }

    public string ToCommandText()
    {
        var argumentList = string.Join(", ", _parameters.Select(x => $"{x.Name} => @{x.Name}"));
        return Mode switch
        {
            DbRoutineCallMode.ScalarFunction => $"select {RoutineName}({argumentList});",
            DbRoutineCallMode.SetReturningFunction => $"select * from {RoutineName}({argumentList});",
            DbRoutineCallMode.Procedure => $"call {RoutineName}({argumentList});",
            _ => throw new InvalidOperationException($"Unsupported routine call mode: {Mode}.")
        };
    }

    private static string NormalizeRoutineParameterName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Routine parameter name is required.", nameof(name));
        }

        var normalized = name.TrimStart('@').Trim();
        if (normalized.Length == 0 || normalized.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_')))
        {
            throw new ArgumentException(
                $"Routine parameter name '{name}' contains unsupported characters.",
                nameof(name));
        }

        return normalized;
    }
}

public enum DbRoutineCallMode
{
    ScalarFunction = 0,
    SetReturningFunction = 1,
    Procedure = 2
}

public readonly record struct DbRoutineParameter(string Name, object? Value);
