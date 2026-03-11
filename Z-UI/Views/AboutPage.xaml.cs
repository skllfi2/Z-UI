using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using ZUI.Services;

namespace ZUI.Views
{
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            this.InitializeComponent();
            LoadInfo();
        }

        private void LoadInfo()
        {
            // Версия приложения
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            AppVersionText.Text = ver != null
                ? $"Версия {ver.Major}.{ver.Minor}.{ver.Build}"
                : "Версия неизвестна";

            // Версия zapret
            ZapretVersionText.Text = ZapretPaths.LocalVersion == "неизвестно"
                ? "Не установлен"
                : $"Версия {ZapretPaths.LocalVersion}";

            // Версия ОС
            try
            {
                var os = Environment.OSVersion;
                OsVersionText.Text = $"Windows {os.Version.Major}.{os.Version.Minor}.{os.Version.Build}";
            }
            catch
            {
                OsVersionText.Text = "Неизвестно";
            }
        }

        private void AppDirButton_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start("explorer.exe", ZapretPaths.AppDir); } catch { }
        }

        private void SettingsDirButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ZUI");
                Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", dir);
            }
            catch { }
        }
    }
}
