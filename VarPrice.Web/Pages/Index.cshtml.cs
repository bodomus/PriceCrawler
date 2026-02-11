using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VarPrice.Web.Crawler;

namespace VarPrice.Web.Pages;

public class IndexModel(CrawlerRunner runner) : PageModel
{
    public CrawlerRunResult? Result { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostIngestVegetablesAsync(CancellationToken ct)
    {
        Result = await runner.RunVegetablesAsync(ct);
        return Page();
    }
}
