// ISoundService.cs - Interface for sound effects management
namespace ZUI.Services;

/// <summary>
/// Service for playing sound effects in the application
/// </summary>
public interface ISoundService
{
    /// <summary>
    /// Gets or sets whether sounds are enabled
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the volume (0.0 to 1.0)
    /// </summary>
    double Volume { get; set; }

    /// <summary>
    /// Play success sound (e.g., protection started, test passed)
    /// </summary>
    void PlaySuccess();

    /// <summary>
    /// Play error sound (e.g., protection failed, test failed)
    /// </summary>
    void PlayError();

    /// <summary>
    /// Play click sound (button clicks, navigation)
    /// </summary>
    void PlayClick();

    /// <summary>
    /// Play notification sound (status changes, warnings)
    /// </summary>
    void PlayNotification();

    /// <summary>
    /// Play toggle sound (switches, checkboxes)
    /// </summary>
    void PlayToggle();

    /// <summary>
    /// Play warning sound (critical issues, errors)
    /// </summary>
    void PlayWarning();

    /// <summary>
    /// Load settings from storage
    /// </summary>
    Task LoadSettingsAsync();

    /// <summary>
    /// Save settings to storage
    /// </summary>
    Task SaveSettingsAsync();
}
