using System.Runtime.InteropServices;

// Windows DLLs used by DiskLens (kernel32, shell32) are only ever loaded from System32.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
