using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;

using PriceCrawler.Application.Features.Categories;
using PriceCrawler.Domain.Categories;

namespace PriceCrawler.Api.Tests;

/// <summary>HTTP behavior of <c>GET /api/v1/stores/{store}/categories</c> and the health endpoint.</summary>
public sealed class CategoryEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CategoryEndpointsTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Categories.Nodes.Clear();
        _factory.Categories.Nodes.AddRange(
        [
            ("varus", new CategoryNode(1, "ovochi", "Овочі", "https://varus.ua/ovochi", null, true, 120)),
            ("varus", new CategoryNode(2, "kartoplia", "Картопля", "https://varus.ua/kartoplia", 1, false, 15)),
            ("other", new CategoryNode(3, "fruits", "Fruits", "https://other.example/fruits", null, false, 7)),
        ]);
    }

    [Fact]
    public async Task TopLevel_ReturnsRootsOfTheStore()
    {
        using HttpClient client = _factory.CreateClient();

        List<CategoryNodeDto>? nodes = await client.GetFromJsonAsync<List<CategoryNodeDto>>(
            "/api/v1/stores/varus/categories");

        CategoryNodeDto root = Assert.Single(nodes!);
        Assert.Equal("ovochi", root.ExternalId);
        Assert.True(root.HasChildren);
        Assert.Equal(120, root.ProductCount);
    }

    [Fact]
    public async Task WithParent_ReturnsChildren()
    {
        using HttpClient client = _factory.CreateClient();

        List<CategoryNodeDto>? nodes = await client.GetFromJsonAsync<List<CategoryNodeDto>>(
            "/api/v1/stores/varus/categories?parentId=1");

        Assert.Equal("Картопля", Assert.Single(nodes!).Name);
    }

    [Fact]
    public async Task UsesCamelCaseJson()
    {
        using HttpClient client = _factory.CreateClient();

        string json = await client.GetStringAsync("/api/v1/stores/varus/categories");

        Assert.Contains("\"externalId\":\"ovochi\"", json, StringComparison.Ordinal);
        Assert.Contains("\"productCount\":120", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/v1/stores/VARUS/categories", "Store")]
    [InlineData("/api/v1/stores/varus/categories?parentId=0", "ParentId")]
    public async Task InvalidInput_Returns400ValidationProblem(string url, string field)
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        ValidationProblemDetails? problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.True(problem!.Errors.ContainsKey(field));
    }

    [Fact]
    public async Task Health_IsUpWithoutDatabase()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
