using System.Net;
using System.Net.Http.Json;

using PriceCrawler.Application.Features.Categories;
using PriceCrawler.Application.Features.Listing;
using PriceCrawler.Application.Features.Products;

namespace PriceCrawler.Api.Tests;

/// <summary>Endpoints against real PostgreSQL with schema version 2 (see <see cref="TestData"/>).</summary>
[Trait("Category", "Integration")]
public sealed class PostgresIntegrationTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task Categories_TopLevel_HaveSubtreeCounts()
    {
        List<CategoryNodeDto> nodes = await GetAsync<List<CategoryNodeDto>>("/api/v1/stores/varus/categories");

        Assert.Equal(["ovochi", "molochni"], nodes.Select(node => node.ExternalId));
        CategoryNodeDto vegetables = nodes[0];
        Assert.True(vegetables.HasChildren);
        Assert.Equal(3, vegetables.ProductCount);
    }

    [Fact]
    public async Task Categories_Children()
    {
        List<CategoryNodeDto> nodes = await GetAsync<List<CategoryNodeDto>>("/api/v1/stores/varus/categories?parentId=1");

        CategoryNodeDto potatoes = Assert.Single(nodes);
        Assert.Equal("Картопля", potatoes.Name);
        Assert.False(potatoes.HasChildren);
    }

    [Fact]
    public async Task Health_Ready_ChecksDatabase()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Products_Default_ActiveObservedProductsOfStoreByName()
    {
        PagedResult<ProductListItemDto> page = await GetProductsAsync("");

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(["Картопля біла", "Картопля рожева", "Молоко 2,5%", "Молоко пробне_100", "Морква"], Names(page));
    }

    [Fact]
    public async Task Products_LatestSnapshotIsUsed()
    {
        PagedResult<ProductListItemDto> page = await GetProductsAsync("filter=externalId:eq:1001");

        ProductListItemDto potato = Assert.Single(page.Items);
        Assert.Equal(25.50m, potato.Price);
        Assert.Equal(30.00m, potato.OldPrice);
        Assert.True(potato.IsPromo);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero), potato.CapturedAt);
        Assert.Equal("кг", potato.PackUnit);
    }

    [Fact]
    public async Task Products_Paging()
    {
        PagedResult<ProductListItemDto> second = await GetProductsAsync("page=2&pageSize=2");
        PagedResult<ProductListItemDto> beyond = await GetProductsAsync("page=9&pageSize=2");

        Assert.Equal(["Молоко 2,5%", "Молоко пробне_100"], Names(second));
        Assert.Equal(3, second.TotalPages);
        Assert.Empty(beyond.Items);
        Assert.Equal(5, beyond.TotalCount);
    }

    [Fact]
    public async Task Products_SortDescendingPutsNullsLast()
    {
        PagedResult<ProductListItemDto> page = await GetProductsAsync("sort=-price");

        Assert.Equal([61.00m, 48.20m, 34.90m, 25.50m, null], page.Items.Select(item => item.Price));
    }

    [Fact]
    public async Task Products_MultipleSortTerms()
    {
        PagedResult<ProductListItemDto> page = await GetProductsAsync("sort=-inStock,name");

        Assert.Equal(["Картопля біла", "Молоко 2,5%", "Молоко пробне_100", "Картопля рожева", "Морква"], Names(page));
    }

    [Theory]
    [InlineData(new[] { "name:contains:МОЛОКО" }, new[] { "Молоко 2,5%", "Молоко пробне_100" })]
    [InlineData(new[] { "name:startswith:карт" }, new[] { "Картопля біла", "Картопля рожева" })]
    [InlineData(new[] { "name:contains:_" }, new[] { "Молоко пробне_100" })]
    [InlineData(new[] { "name:contains:2,5%" }, new[] { "Молоко 2,5%" })]
    [InlineData(new[] { "price:gte:30", "price:lt:60" }, new[] { "Картопля рожева", "Молоко 2,5%" })]
    [InlineData(new[] { "isPromo:eq:true" }, new[] { "Картопля біла", "Молоко 2,5%" })]
    [InlineData(new[] { "inStock:ne:true" }, new[] { "Картопля рожева", "Морква" })]
    [InlineData(new[] { "capturedAt:gt:2026-11-11T00:00:00Z", "oldPrice:gt:40" }, new[] { "Молоко 2,5%" })]
    public async Task Products_Filters(string[] filters, string[] expected)
    {
        string query = string.Join("&", filters.Select(filter => "filter=" + Uri.EscapeDataString(filter)));

        PagedResult<ProductListItemDto> page = await GetProductsAsync(query);

        Assert.Equal(expected, Names(page));
    }

    [Fact]
    public async Task Products_CategoryIncludesSubcategories()
    {
        PagedResult<ProductListItemDto> vegetables = await GetProductsAsync("categoryId=1");
        PagedResult<ProductListItemDto> potatoes = await GetProductsAsync("categoryId=2");
        PagedResult<ProductListItemDto> otherStoreCategory = await GetProductsAsync("categoryId=4");

        Assert.Equal(["Картопля біла", "Картопля рожева", "Морква"], Names(vegetables));
        Assert.Equal(["Картопля біла", "Картопля рожева"], Names(potatoes));
        Assert.Empty(otherStoreCategory.Items);
    }

    [Fact]
    public async Task Products_OtherStore()
    {
        using HttpClient client = factory.CreateClient();

        PagedResult<ProductListItemDto>? page =
            await client.GetFromJsonAsync<PagedResult<ProductListItemDto>>("/api/v1/stores/other/products");

        Assert.Equal(["Apple"], Names(page!));
    }

    [Theory]
    [InlineData("pageSize=500", "PageSize")]
    [InlineData("page=0", "Page")]
    [InlineData("sort=url", "Sort")]
    [InlineData("filter=price:contains:5", "Filter")]
    [InlineData("filter=price:gt:abc", "Filter")]
    [InlineData("filter=nope", "Filter")]
    [InlineData("filter=price:1:5", "Filter")]
    [InlineData("categoryId=-1", "CategoryId")]
    public async Task Products_InvalidParameters_Return400(string query, string field)
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/api/v1/stores/varus/products?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Microsoft.AspNetCore.Mvc.ValidationProblemDetails? problem =
            await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.True(problem!.Errors.ContainsKey(field), string.Join(", ", problem.Errors.Keys));
    }

    private async Task<PagedResult<ProductListItemDto>> GetProductsAsync(string query) =>
        await GetAsync<PagedResult<ProductListItemDto>>($"/api/v1/stores/varus/products?{query}");

    private async Task<T> GetAsync<T>(string url)
    {
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(url);
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static string[] Names(PagedResult<ProductListItemDto> page) => [.. page.Items.Select(item => item.Name)];
}
