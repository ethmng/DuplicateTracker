using System.Runtime.Versioning;

namespace DuplicateTracker;

[SupportedOSPlatform("windows")]
internal static class ShellPicker
{
    const uint PickFolders = 0x20;
    const uint ForceFileSystem = 0x40;
    const uint PathMustExist = 0x800;
    const uint FileMustExist = 0x1000;
    const uint NoChangeDir = 0x8;
    const uint FileSystemPath = 0x80058000;
    const int Cancelled = unchecked((int)0x800704C7);

    public static string? PickFolder(string title) =>
        Show(new FileOpenDialog(), title, PickFolders | ForceFileSystem | PathMustExist | NoChangeDir, save: false, defaultName: null);

    public static string? PickFile(string title) =>
        Show(new FileOpenDialog(), title, ForceFileSystem | FileMustExist | PathMustExist | NoChangeDir, save: false, defaultName: null);

    public static string? PickSaveFile(string title, string defaultName) =>
        Show(new FileSaveDialog(), title, ForceFileSystem | PathMustExist | NoChangeDir, save: true, defaultName);

    internal static void Probe()
    {
        Probe(new FileOpenDialog(), save: false);
        Probe(new FileSaveDialog(), save: true);
    }

    static void Probe(object created, bool save)
    {
        var dialog = (IFileDialog)created;
        try
        {
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | ForceFileSystem | NoChangeDir);
            dialog.SetTitle(AppInfo.Name);
            if (!save)
                return;

            dialog.SetFileTypes(1, [new ComDlgFilterSpec { Name = "CSV", Spec = "*.csv" }]);
            dialog.SetFileTypeIndex(1);
            dialog.SetDefaultExtension("csv");
            dialog.SetFileName("duplicates.csv");
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    static string? Show(object created, string title, uint flags, bool save, string? defaultName)
    {
        var dialog = (IFileDialog)created;
        try
        {
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | flags);
            dialog.SetTitle(title);
            if (save)
            {
                dialog.SetFileTypes(3,
                [
                    new ComDlgFilterSpec { Name = "CSV report", Spec = "*.csv" },
                    new ComDlgFilterSpec { Name = "JSON report", Spec = "*.json" },
                    new ComDlgFilterSpec { Name = "Text report", Spec = "*.txt" }
                ]);
                dialog.SetFileTypeIndex(1);
                dialog.SetDefaultExtension("csv");
                if (!string.IsNullOrWhiteSpace(defaultName))
                    dialog.SetFileName(defaultName);
            }

            var result = dialog.Show(GetConsoleWindow());
            if (result == Cancelled)
                return null;

            if (result < 0)
                Marshal.ThrowExceptionForHR(result);

            dialog.GetResult(out var item);
            try
            {
                return FileSystemPathOf(item);
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    static string? FileSystemPathOf(IShellItem item)
    {
        var result = item.GetDisplayName(FileSystemPath, out var pointer);
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            if (pointer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pointer);
        }
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr GetConsoleWindow();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ComDlgFilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Name;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Spec;
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    class FileOpenDialog
    {
    }

    [ComImport]
    [Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
    class FileSaveDialog
    {
    }

    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ComDlgFilterSpec[] filters);

        void SetFileTypeIndex(uint index);

        void GetFileTypeIndex(out uint index);

        void Advise(IntPtr events, out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(uint options);

        void GetOptions(out uint options);

        void SetDefaultFolder(IShellItem item);

        void SetFolder(IShellItem item);

        void GetFolder(out IShellItem item);

        void GetCurrentSelection(out IShellItem item);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        void GetResult(out IShellItem item);

        void AddPlace(IShellItem item, int place);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);

        void Close(int result);

        void SetClientGuid(ref Guid guid);

        void ClearClientData();

        void SetFilter(IntPtr filter);
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid interfaceId, out IntPtr result);

        void GetParent(out IShellItem parent);

        [PreserveSig]
        int GetDisplayName(uint nameType, out IntPtr name);

        void GetAttributes(uint mask, out uint attributes);

        void Compare(IShellItem other, uint hint, out int order);
    }
}
