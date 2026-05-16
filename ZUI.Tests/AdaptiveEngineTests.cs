// ConverterTests.cs - Unit tests for all IValueConverter implementations
// Covers BoolConverters (20+ converters) and IntConverters (2 converters)
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ZUI.Converters;

namespace ZUI.Tests;

public class BoolToVisibilityConverterTests
{
    private readonly BoolToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsVisible()
    {
        var result = _converter.Convert(true, typeof(Visibility), null!, "");
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_False_ReturnsCollapsed()
    {
        var result = _converter.Convert(false, typeof(Visibility), null!, "");
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_Null_ReturnsCollapsed()
    {
        var result = _converter.Convert(null!, typeof(Visibility), null!, "");
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void ConvertBack_Visible_ReturnsTrue()
    {
        var result = _converter.ConvertBack(Visibility.Visible, typeof(bool), null!, "");
        Assert.True((bool)result);
    }

    [Fact]
    public void ConvertBack_Collapsed_ReturnsFalse()
    {
        var result = _converter.ConvertBack(Visibility.Collapsed, typeof(bool), null!, "");
        Assert.False((bool)result);
    }
}

public class InverseBoolToVisibilityConverterTests
{
    private readonly InverseBoolToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsCollapsed()
    {
        var result = _converter.Convert(true, typeof(Visibility), null!, "");
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_False_ReturnsVisible()
    {
        var result = _converter.Convert(false, typeof(Visibility), null!, "");
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_Null_ReturnsVisible()
    {
        var result = _converter.Convert(null!, typeof(Visibility), null!, "");
        Assert.Equal(Visibility.Visible, result);
    }
}

public class BoolNegationConverterTests
{
    private readonly BoolNegationConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsFalse()
    {
        var result = _converter.Convert(true, typeof(bool), null!, "");
        Assert.False((bool)result);
    }

    [Fact]
    public void Convert_False_ReturnsTrue()
    {
        var result = _converter.Convert(false, typeof(bool), null!, "");
        Assert.True((bool)result);
    }

    [Fact]
    public void Convert_Null_ReturnsFalse()
    {
        var result = _converter.Convert(null!, typeof(bool), null!, "");
        Assert.False((bool)result);
    }

    [Fact]
    public void ConvertBack_True_ReturnsFalse()
    {
        var result = _converter.ConvertBack(true, typeof(bool), null!, "");
        Assert.False((bool)result);
    }

    [Fact]
    public void ConvertBack_False_ReturnsTrue()
    {
        var result = _converter.ConvertBack(false, typeof(bool), null!, "");
        Assert.True((bool)result);
    }
}

public class BoolToStartStopConverterTests
{
    private readonly BoolToStartStopConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsStop()
    {
        var result = _converter.Convert(true, typeof(string), null!, "");
        Assert.Equal("Остановить", result);
    }

    [Fact]
    public void Convert_False_ReturnsStart()
    {
        var result = _converter.Convert(false, typeof(string), null!, "");
        Assert.Equal("Запустить", result);
    }

    [Fact]
    public void Convert_Null_ReturnsStart()
    {
        var result = _converter.Convert(null!, typeof(string), null!, "");
        Assert.Equal("Запустить", result);
    }
}

public class BoolToPlayStopGlyphConverterTests
{
    private readonly BoolToPlayStopGlyphConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsStopGlyph()
    {
        var result = _converter.Convert(true, typeof(string), null!, "");
        Assert.Equal("\uE71B", result);
    }

    [Fact]
    public void Convert_False_ReturnsPlayGlyph()
    {
        var result = _converter.Convert(false, typeof(string), null!, "");
        Assert.Equal("\uE768", result);
    }

    [Fact]
    public void Convert_Null_ReturnsPlayGlyph()
    {
        var result = _converter.Convert(null!, typeof(string), null!, "");
        Assert.Equal("\uE768", result);
    }
}

public class BoolToCheckGlyphConverterTests
{
    private readonly BoolToCheckGlyphConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsCheckGlyph()
    {
        var result = _converter.Convert(true, typeof(string), null!, "");
        Assert.Equal("\uE73E", result);
    }

    [Fact]
    public void Convert_False_ReturnsErrorGlyph()
    {
        var result = _converter.Convert(false, typeof(string), null!, "");
        Assert.Equal("\uE783", result);
    }
}

public class BoolToTextWrappingConverterTests
{
    private readonly BoolToTextWrappingConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsWrap()
    {
        var result = _converter.Convert(true, typeof(TextWrapping), null!, "");
        Assert.Equal(TextWrapping.Wrap, result);
    }

    [Fact]
    public void Convert_False_ReturnsNoWrap()
    {
        var result = _converter.Convert(false, typeof(TextWrapping), null!, "");
        Assert.Equal(TextWrapping.NoWrap, result);
    }
}

public class BoolToStringConverterTests
{
    private readonly BoolToStringConverter _converter = new();

    [Fact]
    public void Convert_True_WithPipeParameter_ReturnsFirstPart()
    {
        var result = _converter.Convert(true, typeof(string), "Остановить|Запустить", "");
        Assert.Equal("Остановить", result);
    }

    [Fact]
    public void Convert_False_WithPipeParameter_ReturnsSecondPart()
    {
        var result = _converter.Convert(false, typeof(string), "Остановить|Запустить", "");
        Assert.Equal("Запустить", result);
    }

    [Fact]
    public void Convert_True_WithoutParameter_ReturnsTrueString()
    {
        var result = _converter.Convert(true, typeof(string), null!, "");
        Assert.Equal("True", result);
    }

    [Fact]
    public void Convert_False_WithoutParameter_ReturnsFalseString()
    {
        var result = _converter.Convert(false, typeof(string), null!, "");
        Assert.Equal("False", result);
    }

    [Fact]
    public void Convert_Null_ReturnsFalseString()
    {
        var result = _converter.Convert(null!, typeof(string), null!, "");
        Assert.Equal("False", result);
    }

    [Fact]
    public void Convert_True_ParameterWithoutPipe_ReturnsTrueString()
    {
        var result = _converter.Convert(true, typeof(string), "NoPipe", "");
        Assert.Equal("True", result);
    }
}

public class BoolToOpacityConverterTests
{
    private readonly BoolToOpacityConverter _converter = new();

    [Fact]
    public void Convert_True_Returns1()
    {
        var result = _converter.Convert(true, typeof(double), null!, "");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void Convert_False_Returns05()
    {
        var result = _converter.Convert(false, typeof(double), null!, "");
        Assert.Equal(0.5, result);
    }

    [Fact]
    public void Convert_Null_Returns05()
    {
        var result = _converter.Convert(null!, typeof(double), null!, "");
        Assert.Equal(0.5, result);
    }
}

public class BoolToInfoBarSeverityConverterTests
{
    private readonly BoolToInfoBarSeverityConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsSuccess()
    {
        var result = _converter.Convert(true, typeof(object), null!, "");
        Assert.Equal(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success, result);
    }

    [Fact]
    public void Convert_False_ReturnsWarning()
    {
        var result = _converter.Convert(false, typeof(object), null!, "");
        Assert.Equal(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning, result);
    }
}

public class StringToVisibilityConverterTests
{
    private readonly StringToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_NonEmptyString_ReturnsVisible()
    {
        var result = _converter.Convert("hello", typeof(Visibility), null!, "");
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_EmptyString_ReturnsCollapsed()
    {
        var result = _converter.Convert("", typeof(Visibility), null!, "");
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_Null_ReturnsCollapsed()
    {
        var result = _converter.Convert(null!, typeof(Visibility), null!, "");
        Assert.Equal(Visibility.Collapsed, result);
    }
}

public class BoolNegationToVisibilityConverterTests
{
    private readonly BoolNegationToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsCollapsed()
    {
        var result = _converter.Convert(true, typeof(Visibility), null!, "");
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_False_ReturnsVisible()
    {
        var result = _converter.Convert(false, typeof(Visibility), null!, "");
        Assert.Equal(Visibility.Visible, result);
    }
}

public class InverseBoolConverterTests
{
    private readonly InverseBoolConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsFalse()
    {
        var result = _converter.Convert(true, typeof(bool), null!, "");
        Assert.False((bool)result);
    }

    [Fact]
    public void Convert_False_ReturnsTrue()
    {
        var result = _converter.Convert(false, typeof(bool), null!, "");
        Assert.True((bool)result);
    }

    [Fact]
    public void Convert_Null_ReturnsTrue()
    {
        var result = _converter.Convert(null!, typeof(bool), null!, "");
        Assert.True((bool)result);
    }
}

public class StringCollectionConverterTests
{
    private readonly StringCollectionConverter _converter = new();

    [Fact]
    public void Convert_StringEnumerable_ReturnsNewlineJoined()
    {
        var lines = new[] { "line1", "line2", "line3" };
        var result = _converter.Convert(lines, typeof(string), null!, "");
        Assert.Equal("line1\nline2\nline3", result);
    }

    [Fact]
    public void Convert_Null_ReturnsEmpty()
    {
        var result = _converter.Convert(null!, typeof(string), null!, "");
        Assert.Equal("", result);
    }
}

public class BoolToCollapseExpandGlyphConverterTests
{
    private readonly BoolToCollapseExpandGlyphConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsCollapseGlyph()
    {
        var result = _converter.Convert(true, typeof(string), null!, "");
        Assert.Equal("\uE972", result);
    }

    [Fact]
    public void Convert_False_ReturnsExpandGlyph()
    {
        var result = _converter.Convert(false, typeof(string), null!, "");
        Assert.Equal("\uE973", result);
    }
}

public class BoolToCollapseExpandTextConverterTests
{
    private readonly BoolToCollapseExpandTextConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsCollapseAll()
    {
        var result = _converter.Convert(true, typeof(string), null!, "");
        Assert.Equal("Свернуть все", result);
    }

    [Fact]
    public void Convert_False_ReturnsExpandAll()
    {
        var result = _converter.Convert(false, typeof(string), null!, "");
        Assert.Equal("Развернуть все", result);
    }
}

public class BoolToDnsIconConverterTests
{
    private readonly BoolToDnsIconConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsGlobeGlyph()
    {
        var result = _converter.Convert(true, typeof(string), null!, "");
        Assert.Equal("\uE893", result);
    }

    [Fact]
    public void Convert_False_ReturnsNetworkTowerGlyph()
    {
        var result = _converter.Convert(false, typeof(string), null!, "");
        Assert.Equal("\uE770", result);
    }
}

public class BoolToCheckWarningGlyphConverterTests
{
    private readonly BoolToCheckWarningGlyphConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsCheckGlyph()
    {
        var result = _converter.Convert(true, typeof(string), null!, "");
        Assert.Equal("\uE73E", result);
    }

    [Fact]
    public void Convert_False_ReturnsWarningGlyph()
    {
        var result = _converter.Convert(false, typeof(string), null!, "");
        Assert.Equal("\uE7BA", result);
    }
}

public class IntToVisibilityConverterTests
{
    private readonly IntToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_MatchingStep_ReturnsVisible()
    {
        var result = _converter.Convert(2, typeof(Visibility), "2", "");
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_NonMatchingStep_ReturnsCollapsed()
    {
        var result = _converter.Convert(1, typeof(Visibility), "2", "");
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_NullValue_ReturnsCollapsed()
    {
        var result = _converter.Convert(null!, typeof(Visibility), "2", "");
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_InvalidParameter_ReturnsCollapsed()
    {
        var result = _converter.Convert(2, typeof(Visibility), "abc", "");
        Assert.Equal(Visibility.Collapsed, result);
    }
}

public class IntToBoolConverterTests
{
    private readonly IntToBoolConverter _converter = new();

    [Fact]
    public void Convert_CurrentGreaterOrEqual_ReturnsTrue()
    {
        var result = _converter.Convert(3, typeof(bool), "2", "");
        Assert.True((bool)result);
    }

    [Fact]
    public void Convert_CurrentEqual_ReturnsTrue()
    {
        var result = _converter.Convert(2, typeof(bool), "2", "");
        Assert.True((bool)result);
    }

    [Fact]
    public void Convert_CurrentLess_ReturnsFalse()
    {
        var result = _converter.Convert(1, typeof(bool), "2", "");
        Assert.False((bool)result);
    }

    [Fact]
    public void Convert_InvalidParameter_ReturnsFalse()
    {
        var result = _converter.Convert(2, typeof(bool), "abc", "");
        Assert.False((bool)result);
    }
}
