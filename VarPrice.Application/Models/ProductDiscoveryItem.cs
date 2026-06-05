namespace VarPrice.Application.Models;

public sealed record ProductDiscoveryItem(
    string Url,
    string? SourceName = null,
    string? SourceUrl = null);
