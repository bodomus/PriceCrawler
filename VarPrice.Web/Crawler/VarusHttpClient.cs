namespace VarPrice.Web.Crawler;

public interface IVarusHttpClient
{
    Task<string> GetStringAsync(Uri uri, CancellationToken ct);
}

public sealed class VarusHttpClient(HttpClient http) : IVarusHttpClient
{
    public async Task<string> GetStringAsync(Uri uri, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
