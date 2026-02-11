using System.Xml.Linq;

namespace VarPrice.Web.Crawler;

public interface ISitemapReader
{
    Task<IReadOnlyList<string>> GetProductUrlsAsync(string sitemapIndexUrl, CancellationToken ct);
}

public sealed class SitemapReader(IHttpClientFactory httpClientFactory) : ISitemapReader
{
    public async Task<IReadOnlyList<string>> GetProductUrlsAsync(string sitemapIndexUrl, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient("varus");
        var xml = await http.GetStringAsync(sitemapIndexUrl, ct);

        var doc = XDocument.Parse(xml);
        var root = doc.Root?.Name.LocalName;

        if (root == "urlset")
            return doc.Descendants().Where(x => x.Name.LocalName == "loc").Select(x => x.Value).ToList();

        if (root == "sitemapindex")
        {
            var sitemapLocs = doc.Descendants().Where(x => x.Name.LocalName == "loc").Select(x => x.Value).ToList();

            var urls = new List<string>(capacity: 50_000);
            foreach (var loc in sitemapLocs.Take(10)) // MVP ограничение
            {
                var partXml = await http.GetStringAsync(loc, ct);
                var partDoc = XDocument.Parse(partXml);

                urls.AddRange(
                    partDoc.Descendants()
                           .Where(x => x.Name.LocalName == "loc")
                           .Select(x => x.Value)
                );

                if (urls.Count > 200_000) break;
            }

            return urls;
        }

        return Array.Empty<string>();
    }
}
