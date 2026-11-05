using PriceCrawler.Domain.Categories;

namespace PriceCrawler.Web.Tests;

public sealed class CategoryTreeSnapshotTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Create_ValidTree_KeepsOrderAndFindsLeaves()
    {
        var tree = CategoryTreeSnapshot.Create("varus",
        [
            Node("frukti-ovochi-gorihi", null),
            Node("frukti-svizhi", "frukti-ovochi-gorihi"),
            Node("banani", "frukti-svizhi"),
            Node("ovochi-svizhi", "frukti-ovochi-gorihi")
        ]);

        Assert.Equal(["frukti-ovochi-gorihi", "frukti-svizhi", "banani", "ovochi-svizhi"],
            tree.Nodes.Select(node => node.ExternalId));
        Assert.Equal(["banani", "ovochi-svizhi"], tree.Leaves.Select(node => node.ExternalId));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_RepeatedKey_FirstOccurrenceWins()
    {
        var tree = CategoryTreeSnapshot.Create("varus",
        [
            Node("healthy", null),
            Node("lactfree-food", "healthy"),
            Node("molochni-produkti", null),
            Node("lactfree-food", "molochni-produkti")
        ]);

        Assert.Equal(1, tree.DuplicateCount);
        Assert.Equal("healthy", tree.Nodes.Single(node => node.ExternalId == "lactfree-food").ParentExternalId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_ParentListedAfterChild_IsRejected()
    {
        var error = Assert.Throws<ArgumentException>(() => CategoryTreeSnapshot.Create("varus",
        [
            Node("banani", "frukti-svizhi"),
            Node("frukti-svizhi", null)
        ]));

        Assert.Contains("frukti-svizhi", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_TrimsValuesAndTreatsBlankParentAsRoot()
    {
        var tree = CategoryTreeSnapshot.Create(" varus ", [new DiscoveredCategory(" banani ", " Банани ", " https://varus.ua/banani ", " ", 0)]);

        var node = Assert.Single(tree.Nodes);
        Assert.Equal("varus", tree.Source);
        Assert.Equal(new DiscoveredCategory("banani", "Банани", "https://varus.ua/banani", null, 0), node);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_BlankName_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => CategoryTreeSnapshot.Create("varus",
            [new DiscoveredCategory("banani", " ", "https://varus.ua/banani", null, 0)]));
    }

    private static DiscoveredCategory Node(string key, string? parent, int order = 0)
        => new(key, key, $"https://varus.ua/{key}", parent, order);
}
