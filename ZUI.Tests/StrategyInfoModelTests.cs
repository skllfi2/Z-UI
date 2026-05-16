// StrategyInfoModelTests.cs - Unit tests for StrategyInfo model
using ZUI.Models;

namespace ZUI.Tests;

public class StrategyInfoModelTests
{
    [Fact]
    public void CreateProgrammatic_SetsCorrectFields()
    {
        var strategy = StrategyInfo.CreateProgrammatic("auto", "Auto (рекомендуется)", "Автоматический перебор");

        Assert.Equal("auto", strategy.Id);
        Assert.Equal("Auto (рекомендуется)", strategy.Name);
        Assert.Equal("Programmatic", strategy.Source);
        Assert.Equal("Автоматический перебор", strategy.Description);
        Assert.True(strategy.IsAvailable);
        Assert.Null(strategy.FilePath);
    }

    [Fact]
    public void CreateProgrammatic_WithoutDescription_SetsNullDescription()
    {
        var strategy = StrategyInfo.CreateProgrammatic("test", "Test");

        Assert.Equal("test", strategy.Id);
        Assert.Null(strategy.Description);
    }

    [Fact]
    public void CreateJson_SetsCorrectFields()
    {
        // Use a non-existent path — IsAvailable should be false
        var strategy = StrategyInfo.CreateJson("/nonexistent/path.json", "My Strategy", "Test desc");

        Assert.Equal("json-path", strategy.Id);
        Assert.Equal("My Strategy", strategy.Name);
        Assert.Equal("JSON", strategy.Source);
        Assert.Equal("/nonexistent/path.json", strategy.FilePath);
        Assert.False(strategy.IsAvailable);
        Assert.Equal("Test desc", strategy.Description);
    }

    [Fact]
    public void CreateJson_WithoutName_UsesFileName()
    {
        var strategy = StrategyInfo.CreateJson("/some/dir/my-strategy.json");

        Assert.Equal("my-strategy", strategy.Name);
        Assert.Equal("json-my-strategy", strategy.Id);
    }

    [Fact]
    public void CreateGenerated_SetsCorrectFields()
    {
        var strategy = StrategyInfo.CreateGenerated("gen-abc", "Custom", "Generated strategy");

        Assert.Equal("gen-abc", strategy.Id);
        Assert.Equal("Custom", strategy.Name);
        Assert.Equal("Generated", strategy.Source);
        Assert.True(strategy.IsAvailable);
        Assert.Equal("Generated strategy", strategy.Description);
    }

    [Fact]
    public void SuccessRate_WithNoRuns_ReturnsZero()
    {
        var strategy = new StrategyInfo();

        Assert.Equal(0, strategy.SuccessRate);
    }

    [Fact]
    public void SuccessRate_WithRuns_ReturnsCorrectPercentage()
    {
        var strategy = new StrategyInfo { SuccessCount = 7, FailCount = 3 };

        // 7 / 10 * 100 = 70
        Assert.Equal(70.0, strategy.SuccessRate);
    }

    [Fact]
    public void SuccessRate_AllSuccess_Returns100()
    {
        var strategy = new StrategyInfo { SuccessCount = 5, FailCount = 0 };

        Assert.Equal(100.0, strategy.SuccessRate);
    }

    [Fact]
    public void TotalRuns_SumsSuccessAndFail()
    {
        var strategy = new StrategyInfo { SuccessCount = 4, FailCount = 2 };

        Assert.Equal(6, strategy.TotalRuns);
    }

    [Fact]
    public void TotalRuns_NoRuns_ReturnsZero()
    {
        var strategy = new StrategyInfo();

        Assert.Equal(0, strategy.TotalRuns);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var strategy = new StrategyInfo();

        Assert.Equal(string.Empty, strategy.Id);
        Assert.Equal(string.Empty, strategy.Name);
        Assert.Equal("JSON", strategy.Source);
        Assert.True(strategy.IsAvailable);
        Assert.Null(strategy.FilePath);
        Assert.Null(strategy.Description);
        Assert.Equal(0, strategy.SuccessCount);
        Assert.Equal(0, strategy.FailCount);
        Assert.Null(strategy.LastUsed);
    }
}
