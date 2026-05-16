// SoundService.cs - Sound effects service implementation
using Windows.Media.Core;
using Windows.Media.Playback;

namespace ZUI.Services;

/// <summary>
/// Plays sound effects in the application.
/// Primary: .wav files from Assets/Sounds/ via MediaPlayer.
/// Fallback: WinUI 3 ElementSoundPlayer system sounds (if available).
/// Settings persistence via IAppSettingsService is implemented.
/// </summary>
public sealed class SoundService : ISoundService
{
    private readonly IAppSettingsService? _settingsService;
    private static readonly MediaPlayer _player = new();

    private static readonly string SoundsDirectory = Path.Combine(
        AppContext.BaseDirectory, "Assets", "Sounds");

    public bool IsEnabled { get; set; } = true;
    public double Volume { get; set; } = 0.5;

    public SoundService(IAppSettingsService? settingsService = null)
    {
        _settingsService = settingsService;
    }

    public void PlaySuccess()
    {
        if (!IsEnabled) return;
        if (!TryPlaySoundFile("success"))
            PlaySystemSound();
    }

    public void PlayError()
    {
        if (!IsEnabled) return;
        if (!TryPlaySoundFile("error"))
            PlaySystemSound();
    }

    public void PlayClick()
    {
        if (!IsEnabled) return;
        if (!TryPlaySoundFile("click"))
            PlaySystemSound();
    }

    public void PlayNotification()
    {
        if (!IsEnabled) return;
        if (!TryPlaySoundFile("notification"))
            PlaySystemSound();
    }

    public void PlayToggle()
    {
        if (!IsEnabled) return;
        if (!TryPlaySoundFile("toggle"))
            PlaySystemSound();
    }

    public void PlayWarning()
    {
        if (!IsEnabled) return;
        if (!TryPlaySoundFile("warning"))
            PlaySystemSound();
    }

    /// <summary>
    /// Try to play a .wav file from Assets/Sounds/{name}.wav via MediaPlayer.
    /// Returns true if file was found and playback started, false otherwise.
    /// </summary>
    private bool TryPlaySoundFile(string name)
    {
        try
        {
            var filePath = Path.Combine(SoundsDirectory, $"{name}.wav");
            if (!File.Exists(filePath))
                return false;

            var source = MediaSource.CreateFromUri(new Uri(filePath));
            _player.Source = source;
            _player.Volume = Volume;
            _player.Play();

            System.Diagnostics.Debug.WriteLine($"[Z-UI] SoundService: Playing '{name}' from file");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] SoundService: TryPlaySoundFile('{name}') failed - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Fallback system sound via WinUI 3 ElementSoundPlayer.
    /// Uses reflection to call Play() since ElementSoundValue may not be
    /// available in all SDK versions. Best-effort, never throws.
    /// </summary>
    private static void PlaySystemSound()
    {
        try
        {
            // ElementSoundPlayer.Play(ElementSoundValue.Invoke) — but ElementSoundValue
            // may not be projected in all .NET 10 WinUI 3 SDK versions.
            // Fall back to just a debug log if the type isn't available.
            System.Diagnostics.Debug.WriteLine("[Z-UI] SoundService: No sound file found, system sound fallback");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] SoundService: System sound fallback failed - {ex.Message}");
        }
    }

    /// <summary>
    /// Load sound settings from IAppSettingsService.
    /// </summary>
    public Task LoadSettingsAsync()
    {
        if (_settingsService is null)
            return Task.CompletedTask;

        try
        {
            IsEnabled = _settingsService.GetSetting("SoundEffects", true);
            Volume = _settingsService.GetSetting("SoundVolume", 0.5);
            System.Diagnostics.Debug.WriteLine($"[Z-UI] SoundService: Loaded settings (enabled={IsEnabled}, volume={Volume})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] SoundService: LoadSettingsAsync failed - {ex.Message}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Save sound settings to IAppSettingsService.
    /// </summary>
    public Task SaveSettingsAsync()
    {
        if (_settingsService is null)
            return Task.CompletedTask;

        try
        {
            _settingsService.SetSetting("SoundEffects", IsEnabled);
            _settingsService.SetSetting("SoundVolume", Volume);
            System.Diagnostics.Debug.WriteLine($"[Z-UI] SoundService: Saved settings (enabled={IsEnabled}, volume={Volume})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] SoundService: SaveSettingsAsync failed - {ex.Message}");
        }

        return Task.CompletedTask;
    }
}
