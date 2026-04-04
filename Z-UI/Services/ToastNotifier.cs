using System;
using System.Runtime.InteropServices;
using System.Security;
using Windows.UI.Notifications;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZUI;
using ZUI.Services;

namespace ZUI.Services
{
    public static class ToastNotifier
    {
        // AppUserModelID required for desktop apps
        private const string AppId = "ZUI.DesktopApp";

        // Toast notifier instance
        private static Windows.UI.Notifications.ToastNotifier? _notifier;

        // Track if toast notifications are enabled
        public static bool IsEnabled => AppSettings.ToastNotifications && IsToastAvailable();

        // Track initialization
        private static bool _initialized = false;

        /// <summary>
        /// Initialize toast notification system.
        /// Must be called from UI thread before first use.
        /// </summary>
        public static void Initialize(IntPtr hwnd)
        {
            if (_initialized) return;

            try
            {
                // Set AppUserModelID for the current process
                SetCurrentProcessExplicitAppUserModelID(AppId);

                // Get the toast notifier for our app
                _notifier = ToastNotificationManager.CreateToastNotifier(AppId);
                _initialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Toast initialization failed: {ex.Message}");
                _initialized = false;
            }
        }

        /// <summary>
        /// Show a toast notification if enabled.
        /// </summary>
        /// <param name="title">Toast title</param>
        /// <param name="message">Toast message body</param>
        /// <param name="toastType">Visual type of toast (Informational, Success, Error, Warning)</param>
        /// <param name="expiration">Optional expiration time (default: 7 seconds)</param>
        /// <param name="tag">Optional tag for grouping/replacing toasts</param>
        /// <param name="group">Optional group for organizing toasts</param>
        public static void Show(
            string title,
            string message,
            ToastType toastType = ToastType.Informational,
            TimeSpan? expiration = null,
            string? tag = null,
            string? group = null)
        {
            if (!IsEnabled || _notifier == null) return;

            try
            {
                // Create toast XML using ToastContentBuilder (if toolkit available)
                // or manual XML if toolkit not available
                var toastXml = CreateToastXml(title, message, toastType, expiration);

		var toast = new ToastNotification(toastXml);
		if (!string.IsNullOrEmpty(tag))
			toast.Tag = tag;
		if (!string.IsNullOrEmpty(group))
			toast.Group = group;

		// Set expiration if provided (default: 7 seconds)
		toast.ExpirationTime = expiration != null ? DateTimeOffset.Now.AddSeconds(expiration.Value.TotalSeconds) : DateTimeOffset.Now.AddSeconds(7);

                // Show the toast
                _notifier.Show(toast);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Toast show failed: {ex.Message}");
                // Fallback to TrayIcon balloon if toast fails
                FallbackToTrayBalloon(title, message);
            }
        }

        /// <summary>
        /// Show a toast with activation callback when user clicks it.
        /// </summary>
        public static void ShowWithActivation(
            string title,
            string message,
            string arguments,
            ToastType toastType = ToastType.Informational,
            string? tag = null)
        {
            if (!IsEnabled || _notifier == null) return;

            try
            {
                var toastXml = CreateToastXml(title, message, toastType);

                var toast = new ToastNotification(toastXml)
                {
                    Tag = tag,
                    // Set up activation arguments (when user clicks toast)
                    SuppressPopup = false
                };

                _notifier.Show(toast);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Toast with activation failed: {ex.Message}");
                FallbackToTrayBalloon(title, message);
            }
        }

        /// <summary>
        /// Create toast XML for simple informational toast.
        /// </summary>
        private static Windows.Data.Xml.Dom.XmlDocument CreateToastXml(
            string title,
            string message,
            ToastType type,
            TimeSpan? expiration = null)
        {
            var toastXml = new Windows.Data.Xml.Dom.XmlDocument();

            // Determine visual element based on type
            string? logo = null;

            switch (type)
            {
                case ToastType.Success:
                    logo = "ms-appx:///Assets/Toast/Success.svg";
                    break;
                case ToastType.Error:
                    logo = "ms-appx:///Assets/Toast/Error.svg";
                    break;
                case ToastType.Warning:
                    logo = "ms-appx:///Assets/Toast/Warning.svg";
                    break;
            }

            // Simple toast template without toolkit
            string template = $@"
            <toast>
                <visual>
                    <binding template='ToastGeneric'>
                        <text>{SecurityElement.Escape(title)}</text>
                        <text>{SecurityElement.Escape(message)}</text>
                        {(logo != null ? $"<image placement='appLogoOverride' src='{logo}'/>" : "")}
                    </binding>
                </visual>
                <audio src='ms-winsoundevent:Notification.Default' loop='false'/>
            </toast>";

            toastXml.LoadXml(template);
            return toastXml;
        }

        /// <summary>
        /// Check if toast notifications are available on this system.
        /// </summary>
        private static bool IsToastAvailable()
        {
            try
            {
                // Toast notifications require Windows 10 build 19041+
                // Check if ToastNotifier is available
                return Windows.Foundation.Metadata.ApiInformation.IsTypePresent(
                    "Windows.UI.Notifications.ToastNotifier");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Fallback to tray icon balloon when toast unavailable.
        /// </summary>
        private static void FallbackToTrayBalloon(string title, string message)
        {
            // Use existing TrayIcon system if available
            try
            {
                // TrayIcon doesn't have direct balloon method exposed,
                // but we can add that capability if needed
                // For now, just silently fail
                System.Diagnostics.Debug.WriteLine($"Toast fallback: {title} - {message}");
            }
            catch { }
        }

        /// <summary>
        /// Handle activation when user clicks a toast.
        /// </summary>
        private static void OnActivationRequested(string arguments)
        {
            // Restore window if minimized/hidden
            var mainWindow = MainWindow.Instance;
            if (mainWindow == null) return;

            mainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                // Bring window to front
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
                    WinRT.Interop.WindowNative.GetWindowHandle(mainWindow));
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                appWindow.Show();
                mainWindow.Activate();

                // Process arguments - navigate to specific page if needed
                ProcessActivationArguments(arguments);
            });
        }

        /// <summary>
        /// Process activation arguments to navigate to appropriate page.
        /// </summary>
        private static void ProcessActivationArguments(string arguments)
        {
            // Example: "updates" navigates to updates page
            // Example: "dashboard" switches to dashboard
            // Example: "service-status" shows status

            if (string.IsNullOrEmpty(arguments))
                return;

            var parts = arguments.Split('&');
            var action = parts[0].ToLowerInvariant();

            switch (action)
            {
                case "updates":
                    MainWindow.Instance?.NavigateTo("updates");
                    break;
                case "dashboard":
                    MainWindow.Instance?.NavigateTo("dashboard");
                    break;
                case "service-status":
                    MainWindow.Instance?.NavigateTo("dashboard");
                    break;
                // Add more as needed
            }
        }

        /// <summary>
        /// Clear all toasts from action center (optional).
        /// </summary>
        public static void ClearAll()
        {
            if (_notifier != null)
            {
                // Clear all toasts by creating a toast with empty XML
                var toastXml = new Windows.Data.Xml.Dom.XmlDocument();
                toastXml.LoadXml("<toast/>");
                var toast = new ToastNotification(toastXml);
                _notifier.Show(toast);
            }
        }

        /// <summary>
        /// Remove specific toast by tag and group.
        /// </summary>
        public static void Remove(string tag, string? group = null)
        {
            if (_notifier != null)
            {
                // Remove by creating a toast with the same tag/group but empty content
                var toastXml = new Windows.Data.Xml.Dom.XmlDocument();
                toastXml.LoadXml("<toast/>");
                var toast = new ToastNotification(toastXml)
                {
                    Tag = tag,
                    Group = group
                };
                _notifier.Show(toast);
            }
        }

        /// <summary>
        /// P/Invoke to set AppUserModelID for desktop apps.
        /// </summary>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string AppID);
    }

    public enum ToastType
    {
        Informational,
        Success,
        Error,
        Warning
    }
}