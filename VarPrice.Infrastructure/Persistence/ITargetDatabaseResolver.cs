namespace VarPrice.Infrastructure.Persistence;

public interface ITargetDatabaseResolver
{
    SelectedDatabase Resolve();
}
