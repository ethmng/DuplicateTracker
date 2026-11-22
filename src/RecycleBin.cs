using System.Runtime.InteropServices;

namespace DuplicateTracker;

internal static class RecycleBin
{
    const uint Delete = 0x0003;
    const ushort Silent = 0x0004;
    const ushort NoConfirmation = 0x0010;
    const ushort AllowUndo = 0x0040;
    const ushort NoErrorUi = 0x0400;

    public static void Send(string path)
    {
        var full = Path.GetFullPath(path);
        // SHFileOperation reads a list of paths that ends with an extra null.
        var buffer = full + "\0\0";
        var from = Marshal.StringToHGlobalUni(buffer);
        try
        {
            var operation = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = Delete,
                pFrom = from,
                pTo = IntPtr.Zero,
                fFlags = (ushort)(Silent | NoConfirmation | AllowUndo | NoErrorUi),
                padding = 0,
                fAnyOperationsAborted = 0,
                hNameMappings = IntPtr.Zero,
                lpszProgressTitle = IntPtr.Zero
            };

            var result = SHFileOperationW(ref operation);
            if (result != 0 || operation.fAnyOperationsAborted != 0)
                throw new IOException($"The Recycle Bin refused {full} (shell code {result}).");
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public ushort padding;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int SHFileOperationW(ref SHFILEOPSTRUCT fileOp);
}
