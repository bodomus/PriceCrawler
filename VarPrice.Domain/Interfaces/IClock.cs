namespace VarPrice.Domain.Interfaces;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
