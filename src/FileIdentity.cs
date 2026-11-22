using System.Runtime.InteropServices;

namespace DuplicateTracker;

internal static class FileIdentity
{
    // FILE_INFO_BY_HANDLE_CLASS.FileIdInfo
    const int FileIdInfoClass = 18;

    public static (string Identity, int LinkCount) Read(SafeFileHandle handle, string fallbackPath)
    {
        var identity = "";
        var links = 1;

        if (GetFileInformationByHandleEx(handle, FileIdInfoClass, out var idInfo, (uint)Marshal.SizeOf<FILE_ID_INFO>()))
        {
            identity = idInfo.VolumeSerialNumber.ToString("X", CultureInfo.InvariantCulture)
                + ":"
                + idInfo.FileIdHigh.ToString("X16", CultureInfo.InvariantCulture)
                + idInfo.FileIdLow.ToString("X16", CultureInfo.InvariantCulture);
        }

        if (GetFileInformationByHandle(handle, out var basic))
        {
            if (basic.NumberOfLinks > 0)
                links = (int)Math.Min(int.MaxValue, basic.NumberOfLinks);

            if (identity.Length == 0)
            {
                identity = basic.VolumeSerialNumber.ToString("X8", CultureInfo.InvariantCulture)
                    + ":"
                    + basic.FileIndexHigh.ToString("X8", CultureInfo.InvariantCulture)
                    + basic.FileIndexLow.ToString("X8", CultureInfo.InvariantCulture);
            }
        }

        if (identity.Length == 0)
            identity = "path:" + PathRules.Normalize(fallbackPath);

        return (identity, Math.Max(1, links));
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FILE_ID_INFO
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int fileInformationClass,
        out FILE_ID_INFO fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION fileInformation);
}
