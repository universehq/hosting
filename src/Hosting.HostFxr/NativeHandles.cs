using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Universe.Hosting.HostFxr;

internal sealed class NativeLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public NativeLibraryHandle(string path)
        : base(ownsHandle: true)
    {
        SetHandle(NativeLibrary.Load(path));
    }

    // Callers hold either the loader lock or a context's reference to this library.
    public nint GetExport(string name) =>
        NativeLibrary.TryGetExport(handle, name, out var pointer)
            ? pointer
            : throw new NativeExportNotFoundException(name);

    protected override bool ReleaseHandle()
    {
        NativeLibrary.Free(handle);
        return true;
    }
}

internal sealed class NativeExportNotFoundException(string exportName)
    : EntryPointNotFoundException($"Required hostfxr export '{exportName}' was not found.")
{
    public string ExportName { get; } = exportName;
}

internal sealed class HostContextHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly nint _close;
    private readonly NativeLibraryHandle _library;

    public HostContextHandle(nint context, nint close, NativeLibraryHandle library)
        : base(ownsHandle: true)
    {
        var added = false;
        library.DangerousAddRef(ref added);
        _library = library;
        _close = close;
        SetHandle(context);
    }

    public NativeLibraryHandle Library => _library;

    protected override unsafe bool ReleaseHandle()
    {
        try
        {
            return ((delegate* unmanaged[Cdecl]<nint, int>)_close)(handle) == 0;
        }
        finally
        {
            _library.DangerousRelease();
        }
    }
}
