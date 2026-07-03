using VarPrice.Domain.Enums;

namespace VarPrice.Application.Models;

public static class VarusPageKindClassifier
{
    public static QueueItemKind Classify(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return QueueItemKind.Unknown;
        }

        return LooksLikeVarusListingUrl(uri)
            ? QueueItemKind.ListingPage
            : QueueItemKind.ProductPage;
    }

    public static bool LooksLikeVarusListingUrl(Uri uri)
    {
        var path = uri.AbsolutePath;

        return path.Contains('~', StringComparison.OrdinalIgnoreCase)
               || path.Contains("~brand_", StringComparison.OrdinalIgnoreCase);
    }
}
