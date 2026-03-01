namespace VarPrice.Web.Storage.Db;

public class DbResult
{
    protected DbResult(bool isSuccess, DbError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DbError? Error { get; }

    public static DbResult Success() => new(true, null);

    public static DbResult Fail(DbError error) => new(false, error);
}

public sealed class DbResult<T> : DbResult
{
    private DbResult(T? value, bool isSuccess, DbError? error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static DbResult<T> Success(T value) => new(value, true, null);

    public new static DbResult<T> Fail(DbError error) => new(default, false, error);
}
