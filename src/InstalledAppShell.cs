using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Pulso;

internal sealed partial class InstalledAppCatalog
{
    internal void ReadStoreApplications() => ReadSource(() =>
    {
        object? shell = null, folder = null, items = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) { partial = true; return; }
            shell = Activator.CreateInstance(shellType);
            if (shell == null) { partial = true; return; }
            folder = ((dynamic)shell).NameSpace("shell:AppsFolder");
            if (folder == null) { partial = true; return; }
            items = ((dynamic)folder).Items();
            if (items == null) { partial = true; return; }
            int count = ((dynamic)items).Count;
            // AppsFolder also lists desktop apps. An empty shell in a profile with desktop entries
            // cannot confirm coverage of that user's packaged apps (e.g. in a restricted session).
            if (count == 0 && apps.Count > 0) partial = true;
            for (int index = 0; index < count; index++)
                ReadSource(() =>
                {
                    object? item = ((dynamic)items).Item(index);
                    if (item == null) return;
                    try
                    {
                        dynamic entry = item;
                        string? id = entry.ExtendedProperty("System.AppUserModel.ID") as string;
                        if (string.IsNullOrWhiteSpace(id)) id = entry.Path as string;
                        int separator = id?.IndexOf('!') ?? -1;
                        if (id == null || separator <= 0 || separator == id.Length - 1 || Path.IsPathRooted(id)) return;
                        string? root = entry.ExtendedProperty("System.AppUserModel.PackageInstallPath") as string;
                        if (string.IsNullOrWhiteSpace(root))
                        {
                            string? package = entry.ExtendedProperty("System.AppUserModel.PackageFullName") as string;
                            if (!string.IsNullOrWhiteSpace(package)) root = PackagePath(package);
                        }
                        if (string.IsNullOrWhiteSpace(root)) { partial = true; return; }
                        ReadPackageApplication(root, id[(separator + 1)..], entry.Name as string);
                    }
                    finally { Release(item); }
                });
        }
        finally { Release(items); Release(folder); Release(shell); }
    });

    private static string? PackagePath(string fullName)
    {
        uint length = 0;
        const int insufficientBuffer = 122;
        if (GetPackagePathByFullName(fullName, ref length, null) != insufficientBuffer || length is 0 or > 32768) return null;
        var path = new StringBuilder((int)length);
        return GetPackagePathByFullName(fullName, ref length, path) == 0 ? path.ToString() : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagePathByFullName(string packageFullName, ref uint pathLength, StringBuilder? path);

    private static (string Path, string Arguments) ShortcutTarget(string file)
    {
        var shortcut = new ShellLink();
        try
        {
            ((IPersistFile)shortcut).Load(file, 0);
            var link = (IShellLinkW)shortcut;
            var target = new StringBuilder(260);
            var arguments = new StringBuilder(4096);
            // SLGP_RAWPATH reads the saved target without invoking shell resolution, link tracking or MSI repair.
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4);
            link.GetArguments(arguments, arguments.Capacity);
            if (target.Length >= target.Capacity - 1 || arguments.Length >= arguments.Capacity - 1) return ("", "");
            return (target.ToString(), arguments.ToString().Trim());
        }
        finally { Release(shortcut); }
    }

    private static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    // Methods through GetArguments, in IShellLinkW vtable order.
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);
        void GetIDList(out IntPtr id);
        void SetIDList(IntPtr id);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int size);
    }
}
