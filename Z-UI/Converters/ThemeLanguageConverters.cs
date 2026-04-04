using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace ZUI.Converters
{
    public class ThemeToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string theme && parameter is string expected)
            {
                return theme?.ToLowerInvariant() switch
                {
                    "light" => expected?.ToString()?.ToLowerInvariant() == "light",
                    "dark" => expected?.ToString()?.ToLowerInvariant() == "dark",
                    _ => expected?.ToString()?.ToLowerInvariant() == "default" || 
                         expected?.ToString()?.ToLowerInvariant() == "системная"
                };
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isChecked && isChecked && parameter is string expected)
            {
                return expected?.ToString()?.ToLowerInvariant() switch
                {
                    "light" => "Светлая",
                    "dark" => "Тёмная",
                    _ => "Системная"
                };
            }
            return "Системная";
        }
    }

    public class LanguageToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string lang && parameter is string expected)
            {
                return lang?.ToLowerInvariant() == expected?.ToString()?.ToLowerInvariant();
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isChecked && isChecked && parameter is string expected)
            {
                return expected?.ToString()?.ToLowerInvariant() switch
                {
                    "ru" => "Русский",
                    "en" => "English",
                    _ => "Русский"
                };
            }
            return "Русский";
        }
    }

    public class ProgressToPercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int progress)
            {
                return $"{progress}%";
            }
            return "0%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToStopPlayGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isRunning)
            {
                return isRunning ? "\xE71A" : "\xE768";
            }
            return "\xE768";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
