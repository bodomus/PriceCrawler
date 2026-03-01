using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Reflection;
using VarPrice.Application.Models;
using VarPrice.Web.Crawler;
using VarPrice.Web.Storage.Db;

namespace VarPrice.Web.Pages;

public class IndexModel(ICrawlerRunner runner, IWebHostEnvironment environment) : PageModel
{
    public CrawlerRunResult? Result { get; private set; }
    public string? StatusMessage { get; private set; }
    public string StatusLevel { get; private set; } = "info";
    public string AppVersion { get; } = ResolveAppVersion();

    public void OnGet() { }

    public async Task<IActionResult> OnPostIngestVegetablesAsync(CancellationToken ct)
    {
        var result = await runner.RunVegetablesAsync(ct);
        if (result.IsFailure)
        {
            SetStatusError(result.Error!);
            return Page();
        }

        Result = result.Value;
        return Page();
    }

    public void SetStatusError(DbError error)
    {
        StatusLevel = "error";
        var message = $"Ошибка при работе с базой данных: {error.UserMessage}";

        if (environment.IsDevelopment() && !string.IsNullOrWhiteSpace(error.TechnicalDetails))
        {
            message += $" Details: {error.TechnicalDetails}";
        }

        if (!string.IsNullOrWhiteSpace(error.CorrelationId))
        {
            message += $" Сообщите код: {error.CorrelationId}";
        }

        StatusMessage = message;
    }

    private static string ResolveAppVersion()
    {
        var assembly = typeof(IndexModel).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
