using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Polyester;
using Universe.Hosting.Common;
using Universe.Hosting.NetHost.Errors;

namespace Universe.Hosting.NetHost;

internal static unsafe partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Parameters
    {
        public nuint Size;
        public void* AssemblyPath;
        public void* DotNetRoot;
    }

    internal delegate int GetHostFxrPathCallback(
        void* buffer,
        nuint* bufferSize,
        Parameters* parameters
    );

    [LibraryImport("nethost", EntryPoint = "get_hostfxr_path")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    internal static partial int GetHostFxrPath(
        void* buffer,
        nuint* bufferSize,
        Parameters* parameters
    );

    internal static Result<string, NetHostError> ResolvePath(
        GetHostFxrParameters? parameters,
        GetHostFxrPathCallback getPath
    )
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                fixed (char* assemblyPath = parameters?.AssemblyPath)
                fixed (char* dotNetRoot = parameters?.DotNetRoot)
                    return ResolvePath(parameters.HasValue, assemblyPath, dotNetRoot, getPath);
            }

            // Pin null-terminated UTF-8 arrays for the full sequence of native calls.
            var assemblyBytes = EncodeUtf8(parameters?.AssemblyPath);
            var rootBytes = EncodeUtf8(parameters?.DotNetRoot);
            fixed (byte* assemblyPath = assemblyBytes)
            fixed (byte* dotNetRoot = rootBytes)
                return ResolvePath(parameters.HasValue, assemblyPath, dotNetRoot, getPath);
        }
        catch (DllNotFoundException)
        {
            return Result<string, NetHostError>.Failure(NetHostError.NativeLibraryNotFound);
        }
        catch (EntryPointNotFoundException)
        {
            return Result<string, NetHostError>.Failure(NetHostError.NativeEntryPointNotFound);
        }
        catch (BadImageFormatException)
        {
            return Result<string, NetHostError>.Failure(NetHostError.NativeLibraryIncompatible);
        }
    }

    private static byte[]? EncodeUtf8(string? value) =>
        value is null ? null : Encoding.UTF8.GetBytes(value + '\0');

    private static Result<string, NetHostError> ResolvePath(
        bool hasParameters,
        void* assemblyPath,
        void* dotNetRoot,
        GetHostFxrPathCallback getPath
    )
    {
        var parameters = new Parameters
        {
            Size = (nuint)sizeof(Parameters),
            AssemblyPath = assemblyPath,
            DotNetRoot = dotNetRoot,
        };
        var characterSize = OperatingSystem.IsWindows() ? sizeof(char) : sizeof(byte);
        nuint capacity = 512;

        // Discovery searches the filesystem on each call. Retry if the path grows between calls.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (capacity > (nuint)(Array.MaxLength / characterSize))
                return Result<string, NetHostError>.Failure(NetHostError.Unrecoverable);

            var buffer = new byte[checked((int)capacity * characterSize)];
            var length = capacity;
            fixed (byte* pointer = buffer)
            {
                var code = getPath(pointer, &length, hasParameters ? &parameters : null);
                if (code == ErrorCodes.HostApiBufferTooSmall)
                {
                    if (length <= capacity)
                        return Result<string, NetHostError>.Failure(NetHostError.BufferTooSmall);
                    capacity = length;
                    continue;
                }
                if (code != ErrorCodes.Success)
                    return Result<string, NetHostError>.Failure(NetHostError.MapErrorCode(code));

                // The native length includes the terminator, in char_t units, not bytes.
                if (length <= 1 || length > capacity)
                    return Result<string, NetHostError>.Failure(NetHostError.Unrecoverable);

                var count = checked((int)length - 1);
                string path;
                if (OperatingSystem.IsWindows())
                {
                    var characters = new ReadOnlySpan<char>(pointer, (int)length);
                    if (characters[count] != '\0' || characters[..count].Contains('\0'))
                        return Result<string, NetHostError>.Failure(NetHostError.Unrecoverable);
                    path = new string(characters[..count]);
                }
                else
                {
                    if (buffer[count] != 0 || buffer.AsSpan(0, count).Contains((byte)0))
                        return Result<string, NetHostError>.Failure(NetHostError.Unrecoverable);
                    path = Encoding.UTF8.GetString(buffer.AsSpan(0, count));
                }
                return Result<string, NetHostError>.Success(path);
            }
        }

        return Result<string, NetHostError>.Failure(NetHostError.BufferTooSmall);
    }
}
