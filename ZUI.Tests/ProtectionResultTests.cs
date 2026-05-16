// ProtectionResultTests.cs - Unit tests for ProtectionResult record
using ZUI.Services;

namespace ZUI.Tests;

public class ProtectionResultTests
{
    [Fact]
    public void Succeeded_SetsSuccessTrue()
    {
        var result = ProtectionResult.Succeeded("general");
        Assert.True(result.Success);
        Assert.Equal("general", result.Strategy);
        Assert.Null(result.Message);
    }

    [Fact]
    public void Failed_SetsSuccessFalse()
    {
        var result = ProtectionResult.Failed("Timeout");
        Assert.False(result.Success);
        Assert.Equal("Timeout", result.Message);
        Assert.Null(result.Strategy);
    }

    [Fact]
    public void ParameterizedConstructor_AllFalse()
    {
        var result = new ProtectionResult(false, null, null);
        Assert.False(result.Success);
        Assert.Null(result.Message);
        Assert.Null(result.Strategy);
    }

    [Fact]
    public void Succeeded_IsRecord_EqualsByValue()
    {
        var a = ProtectionResult.Succeeded("auto");
        var b = ProtectionResult.Succeeded("auto");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Failed_DifferentErrors_NotEqual()
    {
        var a = ProtectionResult.Failed("Error A");
        var b = ProtectionResult.Failed("Error B");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Succeeded_WithMessage_RecordEquality()
    {
        var a = new ProtectionResult(true, "OK", "auto");
        var b = new ProtectionResult(true, "OK", "auto");
        Assert.Equal(a, b);
    }
}
