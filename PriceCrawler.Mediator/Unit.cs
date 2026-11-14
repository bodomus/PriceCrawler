namespace PriceCrawler.Mediator;

/// <summary>The empty result of a command that returns nothing.</summary>
public readonly record struct Unit
{
    /// <summary>The only value of <see cref="Unit"/>.</summary>
    public static Unit Value { get; }
}
