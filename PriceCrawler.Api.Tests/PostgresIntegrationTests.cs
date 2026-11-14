using System.Net;
using System.Net.Http.Json;

using PriceCrawler.Application.Features.Categories;
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
        ProductPageDto page = await GetProductsAsync("");

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(["Картопля біла", "Картопля рожева", "Молоко 2,5%", "Молоко пробне_100", "Морква"], Names(page));
    }

    [Fact]
    public async Task Products_LatestSnapshotIsUsed()
    {
        ProductPageDto page = await GetProductsAsync("filter=externalId:eq:1001");

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
        ProductPageDto second = await GetProductsAsync("page=2&pageSize=2");
        ProductPageDto beyond = await GetProductsAsync("page=9&pageSize=2");

        Assert.Equal(["Молоко 2,5%", "Молоко пробне_100"], Names(second));
        Assert.Equal(3, second.TotalPages);
        Assert.Empty(beyond.Items);
        Assert.Equal(5, beyond.TotalCount);
    }

    [Fact]
    public async Task Products_SortDescendingPutsNullsLast()
    {
        ProductPageDto page = await GetProductsAsync("sort=-price");

        Assert.Equal([61.00m, 48.20m, 34.90m, 25.50m, null], page.Items.Select(item => item.Price));
    }

    [Fact]
    public async Task Products_MultipleSortTerms()
    {
        ProductPageDto page = await GetProductsAsync("sort=-inStock,name");

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

        ProductPageDto page = await GetProductsAsync(query);

        Assert.Equal(expected, Names(page));
    }

    [Fact]
    public async Task Products_CategoryIncludesSubcategories()
    {
        ProductPageDto vegetables = await GetProductsAsync("categoryId=1");
        ProductPageDto potatoes = await GetProductsAsync("categoryId=2");
        ProductPageDto otherStoreCategory = await GetProductsAsync("categoryId=4");

        Assert.Equal(["Картопля біла", "Картопля рожева", "Морква"], Names(vegetables));
        Assert.Equal(["Картопля біла", "Картопля рожева"], Names(potatoes));
        Assert.Empty(otherStoreCategory.Items);
    }

    [Fact]
    public async Task Products_OtherStore()
    {
        using HttpClient client = factory.CreateClient();

        ProductPageDto? page =
            await client.GetFromJsonAsync<ProductPageDto>("/api/v1/stores/other/products");

        Assert.Equal(["Apple"], Names(page!));
    }

    [Fact]
    public async Task Products_DayChange_ComparesWithPreviousKyivDay()
    {
        ProductPageDto page = await GetProductsAsync("");
        var byName = page.Items.ToDictionary(item => item.Name);

        ProductListItemDto potato = byName["Картопля біла"];
        Assert.Equal(30.00m, potato.PreviousPrice);
        Assert.Equal(-4.50m, potato.PriceChange);
        Assert.Equal(-15.00m, potato.PriceChangePercent);

        Assert.Equal(1.90m, byName["Картопля рожева"].PriceChange);
        Assert.Equal(5.76m, byName["Картопля рожева"].PriceChangePercent);

        // 11.11 20:00 UTC is still 11.11 in Kyiv: it is the previous day.
        Assert.Equal(50.00m, byName["Молоко 2,5%"].PreviousPrice);

        // 11.11 23:00 UTC is already 12.11 in Kyiv: same day, so there is nothing to compare with.
        Assert.Null(byName["Молоко пробне_100"].PreviousPrice);
        Assert.Null(byName["Молоко пробне_100"].PriceChange);

        // No current price: no change even though the previous day had one.
        Assert.Equal(12.00m, byName["Морква"].PreviousPrice);
        Assert.Null(byName["Морква"].PriceChange);
    }

    [Fact]
    public async Task Products_SortAndFilterByChange()
    {
        ProductPageDto sorted = await GetProductsAsync("sort=priceChange");
        ProductPageDto falling = await GetProductsAsync("filter=priceChange:lt:0");

        Assert.Equal([-4.50m, -1.80m, 1.90m, null, null], sorted.Items.Select(item => item.PriceChange));
        Assert.Equal(["Картопля біла", "Молоко 2,5%"], Names(falling));
    }

    [Fact]
    public async Task Products_Trend_DailyClosesOfLast30Days()
    {
        ProductPageDto page = await GetProductsAsync("filter=externalId:eq:1001");

        // 01.10 is outside the 30 days before 12.11; on 10.11 the later of two snapshots wins.
        Assert.Equal([30.00m, 25.50m], Assert.Single(page.Items).Trend);
    }

    [Fact]
    public async Task Products_WithoutGrouping_HaveNoGroups()
    {
        ProductPageDto page = await GetProductsAsync("");

        Assert.Empty(page.Groups);
        Assert.All(page.Items, item => Assert.Null(item.GroupKey));
    }

    [Fact]
    public async Task Products_GroupByChange_GroupsComeFirstInOrder()
    {
        ProductPageDto page = await GetProductsAsync("groupBy=change");

        Assert.Equal(
            [("down", 2L), ("up", 1L), ("unknown", 2L)],
            page.Groups.Select(group => (group.Key, group.Count)));
        Assert.Equal(["Картопля біла", "Молоко 2,5%", "Картопля рожева", "Молоко пробне_100", "Морква"], Names(page));
        Assert.Equal(["down", "down", "up", "unknown", "unknown"], page.Items.Select(item => item.GroupKey));
        Assert.Equal(5, page.TotalCount);
    }

    [Fact]
    public async Task Products_GroupByPromo_KeepsSortInsideGroups()
    {
        ProductPageDto page = await GetProductsAsync("groupBy=promo&sort=-price");

        Assert.Equal([("promo", 2L), ("regular", 3L)], page.Groups.Select(group => (group.Key, group.Count)));
        Assert.Equal([48.20m, 25.50m, 61.00m, 34.90m, null], page.Items.Select(item => item.Price));
    }

    [Fact]
    public async Task Products_GroupByCategory_TopLevel()
    {
        ProductPageDto page = await GetProductsAsync("groupBy=category");

        Assert.Equal(
            [("1", "Овочі", 3L), ("3", "Молочні", 2L)],
            page.Groups.Select(group => (group.Key, group.Name, group.Count)));
        Assert.Equal(["1", "1", "1", "3", "3"], page.Items.Select(item => item.GroupKey));
    }

    [Fact]
    public async Task Products_GroupByCategory_SubcategoriesOfRequestedCategory()
    {
        ProductPageDto page = await GetProductsAsync("categoryId=1&groupBy=category");

        Assert.Equal(
            [("2", "Картопля", 2L), ("none", null, 1L)],
            page.Groups.Select(group => (group.Key, group.Name, group.Count)));
        Assert.Equal(["Картопля біла", "Картопля рожева", "Морква"], Names(page));
    }

    [Fact]
    public async Task Products_GroupedPaging_SpansGroups()
    {
        ProductPageDto page = await GetProductsAsync("groupBy=change&page=2&pageSize=2");

        Assert.Equal(["Картопля рожева", "Молоко пробне_100"], Names(page));
        Assert.Equal(3, page.Groups.Count);
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
    [InlineData("groupBy=brand", "GroupBy")]
    public async Task Products_InvalidParameters_Return400(string query, string field)
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/api/v1/stores/varus/products?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Microsoft.AspNetCore.Mvc.ValidationProblemDetails? problem =
            await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.True(problem!.Errors.ContainsKey(field), string.Join(", ", problem.Errors.Keys));
    }

    private async Task<ProductPageDto> GetProductsAsync(string query) =>
        await GetAsync<ProductPageDto>($"/api/v1/stores/varus/products?{query}");

    private async Task<T> GetAsync<T>(string url)
    {
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(url);
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static string[] Names(ProductPageDto page) => [.. page.Items.Select(item => item.Name)];
}
