using System.Xml.Linq;

namespace VarPrice.Web.Crawler;

public enum SitemapDocKind { SitemapIndex, UrlSet, Unknown }

public interface ISitemapParser
{
    SitemapDocKind Detect(string xml);
    IReadOnlyList<Uri> ParseSitemapIndexLocs(string xml);
    IReadOnlyList<Uri> ParseUrlSetLocs(string xml);
}

public sealed class SitemapParser : ISitemapParser
{
    public SitemapDocKind Detect(string xml)
    {
        var root = XDocument.Parse(xml).Root?.Name.LocalName;
        return root switch
        {
            "sitemapindex" => SitemapDocKind.SitemapIndex,
            "urlset" => SitemapDocKind.UrlSet,
            _ => SitemapDocKind.Unknown
        };
    }

    public IReadOnlyList<Uri> ParseSitemapIndexLocs(string xml)
        => ParseLocs(xml, "sitemap");

    public IReadOnlyList<Uri> ParseUrlSetLocs(string xml)
        => ParseLocs(xml, "url");

    private static IReadOnlyList<Uri> ParseLocs(string xml, string parentName)
    {
        var doc = XDocument.Parse(xml);
        return doc.Descendants()
            .Where(x => x.Name.LocalName == parentName)
            .Elements()
            .Where(x => x.Name.LocalName == "loc")
            .Select(x => x.Value.Trim())
            .Where(v => Uri.TryCreate(v, UriKind.Absolute, out _))
            .Select(v => new Uri(v))
            .ToList();
    }
}
