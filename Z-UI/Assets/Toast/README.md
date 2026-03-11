# Toast Notification Icons

This directory contains icons used for toast notifications in ZUI.

## Icon Files

- `Success.png` - Green checkmark icon for successful operations
- `Error.png` - Red error icon for error notifications
- `Warning.png` - Yellow warning icon for warning notifications

## Icon Specifications

- **Format**: PNG with transparency
- **Size**: 32x32 pixels (recommended for toast notifications)
- **Color**: White icons on transparent background
- **Style**: Simple, clean, and easily recognizable

## Usage

These icons are referenced in the ToastNotifier class to provide visual context for different types of notifications:

- Success: Green checkmark for successful operations
- Error: Red error icon for error notifications
- Warning: Yellow warning icon for warning notifications

## Generation

You can generate these icons using any image editor or use online tools like:
- [favicon.io](https://favicon.io/)
- [RealFaviconGenerator](https://realfavicongenerator.net/)
- Or create them manually with icons from libraries like Font Awesome

## Example Icons

For reference, you might want to use icons like:
- Success: ✓ (check mark)
- Error: ⚠ (warning sign) or ✗ (multiplication x)
- Warning: ⚠ (warning sign)

## Integration

The ToastNotifier class references these icons using ms-appx:/// protocol:
```csharp
logo = "ms-appx:///Assets/Toast/Success.png";
```

Ensure these files are included in your project and set to copy to output directory.