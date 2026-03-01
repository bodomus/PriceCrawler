using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using VarPrice.Application.Models;
using VarPrice.Web.Crawler;
using VarPrice.Web.Pages;
using VarPrice.Web.Storage.Db;

namespace VarPrice.Web.Tests;

public sealed class IndexModelTests
{
    [Fact]
    public async Task OnPostIngestVegetablesAsync_WhenDbFail_SetsStatusBarMessage()
    {
        var error = new DbError(
            DbErrorCodes.Connection,
            "Не удалось подключиться к базе данных.",
            "Host=localhost;Password=secret",
            "PgCrawlerRepository.StartRunAsync",
            "ABC123");

        var runner = new FakeCrawlerRunner(DbResult<CrawlerRunResult>.Fail(error));
        var model = new IndexModel(runner, new FakeWebHostEnvironment { EnvironmentName = Environments.Production });

        var actionResult = await model.OnPostIngestVegetablesAsync(CancellationToken.None);

        Assert.IsType<PageResult>(actionResult);
        Assert.Equal("error", model.StatusLevel);
        Assert.Contains("Ошибка при работе с базой данных", model.StatusMessage);
        Assert.Contains("Не удалось подключиться к базе данных.", model.StatusMessage);
        Assert.Contains("ABC123", model.StatusMessage);
        Assert.DoesNotContain("Host=localhost", model.StatusMessage);
    }

    private sealed class FakeCrawlerRunner(DbResult<CrawlerRunResult> response) : ICrawlerRunner
    {
        public Task<DbResult<CrawlerRunResult>> RunVegetablesAsync(CancellationToken ct) => Task.FromResult(response);
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "VarPrice.Web.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public string EnvironmentName { get; set; } = Environments.Production;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
    }
}
