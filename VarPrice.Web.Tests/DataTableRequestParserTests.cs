using Microsoft.AspNetCore.Http;
using VarPrice.Application.Grids;

namespace VarPrice.Web.Tests;

public sealed class DataTableRequestParserTests
{
    private static readonly IDataTableRequestParser Parser = new DataTableRequestParser();

    [Fact]
    public void Parse_Returns_Defaults_When_Request_Has_No_Form()
    {
        var context = new DefaultHttpContext();

        var result = Parser.Parse(context.Request);

        Assert.Equal(0, result.Draw);
        Assert.Equal(0, result.Start);
        Assert.Equal(25, result.Length);
        Assert.Null(result.SearchValue);
        Assert.Equal(0, result.OrderColumn);
        Assert.True(result.OrderAscending);
    }

    [Fact]
    public void Parse_Reads_And_Normalizes_DataTables_Fields()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["draw"] = "7",
            ["start"] = "30",
            ["length"] = "500",
            ["search[value]"] = "   done   ",
            ["order[0][column]"] = "2",
            ["order[0][dir]"] = "desc"
        });

        var result = Parser.Parse(context.Request);

        Assert.Equal(7, result.Draw);
        Assert.Equal(30, result.Start);
        Assert.Equal(200, result.Length);
        Assert.Equal("done", result.SearchValue);
        Assert.Equal(2, result.OrderColumn);
        Assert.False(result.OrderAscending);
    }
}
