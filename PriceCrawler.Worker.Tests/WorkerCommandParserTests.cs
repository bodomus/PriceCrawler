namespace PriceCrawler.Worker.Tests;

public sealed class WorkerCommandParserTests
{
    [Theory]
    [InlineData("vegetables", WorkerRunMode.Vegetables)]
    [InlineData("catalog-refresh", WorkerRunMode.CatalogRefresh)]
    [InlineData("collect-prices", WorkerRunMode.CollectPrices)]
    [InlineData("run-all", WorkerRunMode.RunAll)]
    public void Parse_PositionalCommand_SelectsMode(string command, WorkerRunMode expectedMode)
    {
        var result = WorkerCommandParser.Parse([command]);

        Assert.True(result.IsValid);
        Assert.False(result.ShowHelp);
        Assert.NotNull(result.Command);
        Assert.Equal(expectedMode, result.Command.Mode);
        Assert.False(result.Command.Once);
    }

    [Fact]
    public void Parse_VegetablesOnce_SetsLegacyOnceFlag()
    {
        var result = WorkerCommandParser.Parse(["vegetables", "--once"]);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Command);
        Assert.Equal(WorkerRunMode.Vegetables, result.Command.Mode);
        Assert.True(result.Command.Once);
    }

    [Theory]
    [InlineData("catalog-refresh")]
    [InlineData("collect-prices")]
    [InlineData("run-all")]
    public void Parse_OnceWithUnsupportedMode_ReturnsInvalidResult(string mode)
    {
        var result = WorkerCommandParser.Parse([mode, "--once"]);

        Assert.False(result.IsValid);
        Assert.Equal("--once is only supported for vegetables.", result.ErrorMessage);
    }

    [Fact]
    public void Parse_LegacyCatalogRefreshOnce_ReturnsInvalidResult()
    {
        var result = WorkerCommandParser.Parse(["--job", "catalog-refresh", "--once"]);

        Assert.False(result.IsValid);
        Assert.Equal("--once is only supported for vegetables.", result.ErrorMessage);
    }

    [Fact]
    public void Parse_LegacyCollectPricesOnce_ReturnsInvalidResult()
    {
        var result = WorkerCommandParser.Parse(["--collect-prices", "--once"]);

        Assert.False(result.IsValid);
        Assert.Equal("--once is only supported for vegetables.", result.ErrorMessage);
    }

    [Theory]
    [InlineData("--job", "vegetables", WorkerRunMode.Vegetables)]
    [InlineData("--job", "catalog-refresh", WorkerRunMode.CatalogRefresh)]
    [InlineData("--job", "collect-prices", WorkerRunMode.CollectPrices)]
    [InlineData("--job", "run-all", WorkerRunMode.RunAll)]
    public void Parse_LegacyJobOption_SelectsMode(string option, string value, WorkerRunMode expectedMode)
    {
        var result = WorkerCommandParser.Parse([option, value]);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Command);
        Assert.Equal(expectedMode, result.Command.Mode);
    }

    [Fact]
    public void Parse_LegacyCollectPricesFlag_SelectsCollectPrices()
    {
        var result = WorkerCommandParser.Parse(["--collect-prices"]);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Command);
        Assert.Equal(WorkerRunMode.CollectPrices, result.Command.Mode);
    }

    [Fact]
    public void Parse_NoArguments_UsesLegacyVegetablesDefault()
    {
        var result = WorkerCommandParser.Parse([]);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Command);
        Assert.Equal(WorkerRunMode.Vegetables, result.Command.Mode);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Parse_HelpArgument_DoesNotCreateCommand(string argument)
    {
        var result = WorkerCommandParser.Parse([argument]);

        Assert.True(result.IsValid);
        Assert.True(result.ShowHelp);
        Assert.Null(result.Command);
    }

    [Fact]
    public void Parse_UnsupportedCommand_ReturnsInvalidResult()
    {
        var result = WorkerCommandParser.Parse(["unknown"]);

        Assert.False(result.IsValid);
        Assert.Equal("Unsupported command: unknown", result.ErrorMessage);
    }

    [Fact]
    public void Parse_MissingJobValue_ReturnsInvalidResult()
    {
        var result = WorkerCommandParser.Parse(["--job"]);

        Assert.False(result.IsValid);
        Assert.Equal("Missing value for --job.", result.ErrorMessage);
    }

    [Fact]
    public void Parse_ConflictingCommands_ReturnsInvalidResult()
    {
        var result = WorkerCommandParser.Parse(["catalog-refresh", "collect-prices"]);

        Assert.False(result.IsValid);
        Assert.Equal("Conflicting worker commands: catalog-refresh and collect-prices.", result.ErrorMessage);
    }
}
