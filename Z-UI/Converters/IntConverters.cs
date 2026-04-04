using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ZUI.Converters;

public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int currentStep && int.TryParse(parameter?.ToString(), out int expectedStep))
            return currentStep == expectedStep ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class IntToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int currentStep && int.TryParse(parameter?.ToString(), out int expectedStep))
            return currentStep >= expectedStep;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
