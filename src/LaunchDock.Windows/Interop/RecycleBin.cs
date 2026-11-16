using System.Runtime.InteropServices;

namespace LaunchDock.Windows.Interop;

/// <summary>通过 Shell 把文件或目录移入回收站，失败时退化为永久删除。</summary>
public static class RecycleBin
{
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;

    public static bool Delete(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            var operation = new ShFileOpStruct
            {
                Func = FoDelete,
                // SHFileOperation 要求路径列表以双 null 结尾
                From = Path.GetFullPath(path) + '\0',
                Flags = FofSilent | FofNoConfirmation | FofAllowUndo | FofNoErrorUi
            };

            var result = SHFileOperation(ref operation);
            if (result == 0 && !operation.AnyOperationsAborted)
            {
                return true;
            }
        }
        catch
        {
            // 回收站不可用时走永久删除
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr Hwnd;
        public uint Func;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string From;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? To;

        public ushort Flags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;

        public IntPtr NameMappings;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct fileOp);
}
