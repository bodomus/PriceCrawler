namespace VarPrice.Infrastructure.Persistence;

public sealed record SelectedDatabase(
    DatabaseTarget Target,
    string ConnectionString,
    string DatabaseName);
