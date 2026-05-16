// ═══════════════════════════════════════════════════════════════
// ZUI.Core / WinDivert / SafeWinDivertHandle.cs
// SafeHandle для WinDivert — гарантирует закрытие при GC/приложении
// ═══════════════════════════════════════════════════════════════

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ZUI.Core.WinDivert;

/// <summary>
/// SafeHandle для WinDivert. Автоматически вызывает WinDivertClose при освобождении.
/// IntPtr == 0 или -1 считается невалидным (SafeHandleZeroOrMinusOneIsInvalid).
/// </summary>
public sealed class SafeWinDivertHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeWinDivertHandle() : base(ownsHandle: true) { }

    public SafeWinDivertHandle(IntPtr handle) : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        return WinDivertNative.WinDivertClose(handle);
    }
}
