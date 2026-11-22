using System.Runtime.InteropServices;

internal static class TestProcess
{
    [DllImport("kernel32.dll")] private static extern uint GetErrorMode();
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);

    public static int Run(Func<int> checks)
    {
        if (OperatingSystem.IsWindows())
        {
            const uint noCrashDialog = 0x0002; // SEM_NOGPFAULTERRORBOX, only for this test process.
            SetErrorMode(GetErrorMode() | noCrashDialog);
        }
        try { return checks(); }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
