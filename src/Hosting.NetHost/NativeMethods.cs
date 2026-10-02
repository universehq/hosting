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
                    return ResolvePath(parameters, assemblyPath, dotNetRoot, getPath);
            }

            // Pin null-terminated UTF-8 arrays for the full sequence of native calls.
            var assemblyBytes = EncodeUtf8(parameters?.AssemblyPath);
            var rootBytes = EncodeUtf8(parameters?.DotNetRoot);
            fixed (byte* assemblyPath = assemblyBytes)
            fixed (byte* dotNetRoot = rootBytes)
                return ResolvePath(parameters, assemblyPath, dotNetRoot, getPath);
        }
        catch (DllNotFoundException error)
        {
            return Result<string, NetHostError>.Failure(new NativeLibraryNotFound("nethost", error));
        }
        catch (EntryPointNotFoundException error)
        {
            return Result<string, NetHostError>.Failure(
                new NativeEntryPointNotFound("nethost", "get_hostfxr_path", error)
            );
        }
        catch (BadImageFormatException error)
        {
            return Result<string, NetHostError>.Failure(new NativeLibraryIncompatible("nethost", error));
        }
    }

    private static byte[]? EncodeUtf8(string? value) =>
        value is null ? null : Encoding.UTF8.GetBytes(value + '\0');

    private static Result<string, NetHostError> ResolvePath(
        GetHostFxrParameters? options,
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
        var bufferError = new BufferTooSmall(capacity, capacity);

        // Discovery searches the filesystem on each call. Retry if the path grows between calls.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buffer = new byte[checked((int)capacity * characterSize)];
            var length = capacity;
            fixed (byte* pointer = buffer)
            {
                var code = getPath(pointer, &length, options.HasValue ? &parameters : null);
                if (code == ErrorCodes.HostApiBufferTooSmall)
                {
                    bufferError = new BufferTooSmall(capacity, length);
                    if (length <= capacity)
                        return Result<string, NetHostError>.Failure(bufferError);
                    if (length > (nuint)(Array.MaxLength / characterSize))
                        return Result<string, NetHostError>.Failure(
                            new InvalidNativeResponse(
                                "requested buffer exceeds managed array limits", capacity, length
                            )
                        );
                    capacity = length;
                    continue;
                }
                if (code != ErrorCodes.Success)
                    return Result<string, NetHostError>.Failure(NetHostError.MapErrorCode(code, options));

                // The native length includes the terminator, in char_t units, not bytes.
                if (length <= 1 || length > capacity)
                    return Result<string, NetHostError>.Failure(
                        new InvalidNativeResponse(
                            "path length must include a nonempty path and terminator within the buffer", capacity, length
                        )
                    );

                var count = checked((int)length - 1);
                string path;
                if (OperatingSystem.IsWindows())
                {
                    var characters = new ReadOnlySpan<char>(pointer, (int)length);
                    if (characters[count] != '\0' || characters[..count].Contains('\0'))
                        return Result<string, NetHostError>.Failure(
                            new InvalidNativeResponse(
                                "path contains an embedded null or is not null-terminated", capacity, length
                            )
                        );
                    path = new string(characters[..count]);
                }
                else
                {
                    if (buffer[count] != 0 || buffer.AsSpan(0, count).Contains((byte)0))
                        return Result<string, NetHostError>.Failure(
                            new InvalidNativeResponse(
                                "path contains an embedded null or is not null-terminated", capacity, length
                            )
                        );
                    path = Encoding.UTF8.GetString(buffer.AsSpan(0, count));
                }
                return Result<string, NetHostError>.Success(path);
            }
        }

        return Result<string, NetHostError>.Failure(bufferError);
    }
}
