using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VarPrice.Application.Models;
using VarPrice.Application.UseCases;

namespace VarPrice.Web.Pages;

public class IndexModel(RunCrawlerUseCase runner) : PageModel
{
    public CrawlerRunResult? Result { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostIngestVegetablesAsync(CancellationToken ct)
    {
        Result = await runner.RunVegetablesAsync(ct);
        return Page();
    }
}
