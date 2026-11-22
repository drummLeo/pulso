using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Pulso;

// All notifications are delivered to the WPF window's thread, including the out-of-context foreground hook.
internal sealed class WindowsUsageSession : IDisposable
{
    private const int WmPowerBroadcast = 0x218, PowerSettingChange = 0x8013, WmSessionChange = 0x2b1;
    private const uint SystemForeground = 3;
    private static readonly Guid DisplayStatus = new("2b84c20e-ad23-4ddf-93db-05ffbd7efca5");
    private readonly HwndSource source;
    private readonly Action interrupt, checkpoint;
    private readonly ForegroundCallback foregroundCallback;
    private readonly nint window, displayRegistration;
    private readonly bool sessionRegistered;
    private nint foregroundHook;
    private bool unlocked, displayOn, suspended;

    private delegate void ForegroundCallback(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint first, uint last, nint module, ForegroundCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint RegisterPowerSettingNotification(nint window, in Guid setting, uint flags);
    [DllImport("user32.dll")] private static extern bool UnregisterPowerSettingNotification(nint handle);
    [DllImport("wtsapi32.dll")] private static extern bool WTSRegisterSessionNotification(nint window, uint flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(nint window);
    [DllImport("wtsapi32.dll")] private static extern bool WTSQuerySessionInformationW(nint server, int session, int infoClass, out nint data, out uint size);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(nint data);

    // Prefix of WTSINFOEXW, whose level-one union is eight-byte aligned on the win-x64 target.
    [StructLayout(LayoutKind.Explicit)]
    private struct SessionInfo
    {
        [FieldOffset(0)] public int Level;
        [FieldOffset(12)] public int Connection;
        [FieldOffset(16)] public int LockState;
    }

    public WindowsUsageSession(HwndSource source, Action interrupt, Action checkpoint)
    {
        this.source = source;
        this.interrupt = interrupt;
        this.checkpoint = checkpoint;
        foregroundCallback = (_, _, _, _, _, _, _) => interrupt();
        window = source.Handle;
        source.AddHook(Message);
        sessionRegistered = WTSRegisterSessionNotification(window, 0);
        unlocked = IsUnlocked();
        displayRegistration = RegisterPowerSettingNotification(window, in DisplayStatus, 0);
    }

    private static bool IsUnlocked()
    {
        const int currentSession = -1, sessionInfoEx = 25;
        if (!WTSQuerySessionInformationW(0, currentSession, sessionInfoEx, out var buffer, out uint size)) return false;
        try
        {
            if (size < Marshal.SizeOf<SessionInfo>()) return false;
            var info = Marshal.PtrToStructure<SessionInfo>(buffer);
            return info.Level == 1 && info.Connection == 0 && info.LockState == 1;
        }
        finally { WTSFreeMemory(buffer); }
    }

    public bool CanMeasure => displayOn && !suspended && (sessionRegistered ? unlocked : IsUnlocked());

    public void WatchForeground(bool enabled)
    {
        if (enabled && foregroundHook == 0) foregroundHook = SetWinEventHook(SystemForeground, SystemForeground, 0, foregroundCallback, 0, 0, 0);
        if (!enabled && foregroundHook != 0) { UnhookWinEvent(foregroundHook); foregroundHook = 0; }
    }

    private nint Message(nint handle, int message, nint action, nint detail, ref bool handled)
    {
        if (message == WmSessionChange)
        {
            switch ((int)action)
            {
                case 2: case 4: case 6: case 7: // Disconnect, logoff, lock.
                    unlocked = false;
                    break;
                case 8: unlocked = true; break; // WTS_SESSION_UNLOCK.
                case 1: case 3: case 5: case 15: // Connect, logon or desktop ready.
                    unlocked = IsUnlocked();
                    break;
                default: return 0;
            }
        }
        else if (message == WmPowerBroadcast)
        {
            switch ((int)action)
            {
                case 4: suspended = true; break;
                case 6: case 7: case 18: suspended = false; unlocked = IsUnlocked(); break;
                case PowerSettingChange:
                    if (detail == 0 || Marshal.PtrToStructure<Guid>(detail) != DisplayStatus || Marshal.ReadInt32(detail, 16) != 4) return 0;
                    displayOn = Marshal.ReadInt32(detail, 20) is 1 or 2;
                    break;
                default: return 0;
            }
        }
        else return 0;
        interrupt();
        if (!CanMeasure) checkpoint();
        return 0;
    }

    public void Dispose()
    {
        WatchForeground(false);
        if (displayRegistration != 0) UnregisterPowerSettingNotification(displayRegistration);
        if (sessionRegistered) WTSUnRegisterSessionNotification(window);
        if (!source.IsDisposed) source.RemoveHook(Message);
    }
}
