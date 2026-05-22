namespace VarPrice.Application.Abstractions;

public interface IProductUrlFilter
{
    IReadOnlyList<string> Apply(IEnumerable<Uri> urls, string sourceName);
}
