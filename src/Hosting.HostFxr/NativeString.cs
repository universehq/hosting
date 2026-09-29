using System.Runtime.InteropServices;

namespace Universe.Hosting.HostFxr;

/// <summary>Owns a null-terminated host char_t string (UTF-16 on Windows, UTF-8 elsewhere).</summary>
internal sealed class NativeString : IDisposable
{
    private nint _pointer;

    public NativeString(string? value)
    {
        if (value is not null)
        {
            Validate(value, nameof(value), allowEmpty: true);
            _pointer = OperatingSystem.IsWindows()
                ? Marshal.StringToCoTaskMemUni(value)
                : Marshal.StringToCoTaskMemUTF8(value);
        }
    }

    public nint Pointer => _pointer;

    public static string? Read(nint pointer) => OperatingSystem.IsWindows()
        ? Marshal.PtrToStringUni(pointer)
        : Marshal.PtrToStringUTF8(pointer);

    public static void Validate(string value, string parameterName, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if ((!allowEmpty && value.Length == 0) || value.Contains('\0'))
            throw new ArgumentException("The string must be nonempty and contain no null characters.", parameterName);
    }

    public void Dispose()
    {
        Marshal.FreeCoTaskMem(Interlocked.Exchange(ref _pointer, 0));
    }
}