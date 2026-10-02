using Failure.CompilerServices;
using Universe.Hosting.Common;

namespace Universe.Hosting.NetHost.Errors;

/// <summary>Failures encountered while discovering the .NET host.</summary>
[FailureImpl]
public readonly partial union NetHostError(
    InvalidArgFailure,
    HostFxrNotFound,
    HostFxrLoadFailure,
    HostFxrEntryPointNotFound,
    CurrentHostNotFound,
    BufferTooSmall,
    NativeLibraryNotFound,
    NativeEntryPointNotFound,
    NativeLibraryIncompatible,
    InvalidNativeResponse,
    UnknownNativeError
);

[Failure("Native operation '{Operation}' received an invalid argument.")]
public readonly record struct InvalidArgFailure(string Operation);

[Failure("Could not find hostfxr (dotnet root: '{DotNetRoot}', assembly: '{AssemblyPath}').")]
public readonly record struct HostFxrNotFound(string? DotNetRoot, string? AssemblyPath);

[Failure("The discovered hostfxr library could not be loaded.")]
public readonly record struct HostFxrLoadFailure;

[Failure("The discovered hostfxr library is missing a required entry point.")]
public readonly record struct HostFxrEntryPointNotFound;

[Failure("The current native host could not be found.")]
public readonly record struct CurrentHostNotFound;

[Failure("Native discovery reported a buffer too small (capacity: {Capacity}, reported required length: {RequiredLength}).")]
public readonly record struct BufferTooSmall(nuint Capacity, nuint RequiredLength);

[Failure("Native library '{LibraryName}' could not be loaded.")]
public readonly record struct NativeLibraryNotFound(string LibraryName, DllNotFoundException Exception);

[Failure("Native export '{ExportName}' was not found in '{LibraryName}'.")]
public readonly record struct NativeEntryPointNotFound(
    string LibraryName, string ExportName, EntryPointNotFoundException Exception);

[Failure("Native library '{LibraryName}' is incompatible with this process.")]
public readonly record struct NativeLibraryIncompatible(string LibraryName, BadImageFormatException Exception);

[Failure("Native hostfxr discovery returned an invalid response: {Reason} (capacity: {Capacity}, length: {Length}).")]
public readonly record struct InvalidNativeResponse(string Reason, nuint Capacity, nuint Length);

[Failure("'{Operation}' failed with native code 0x{Code:X8}.")]
public readonly record struct UnknownNativeError(string Operation, int Code);

internal static class NetHostErrorExtensions
{
    extension(NetHostError)
    {
        public static NetHostError MapErrorCode(int code, GetHostFxrParameters? parameters) =>
            code switch
            {
                ErrorCodes.InvalidArgFailure => new InvalidArgFailure("get_hostfxr_path"),
                ErrorCodes.CoreHostLibMissingFailure =>
                    new HostFxrNotFound(parameters?.DotNetRoot, parameters?.AssemblyPath),
                ErrorCodes.CoreHostLibLoadFailure => new HostFxrLoadFailure(),
                ErrorCodes.CoreHostEntryPointFailure => new HostFxrEntryPointNotFound(),
                ErrorCodes.CoreHostCurHostFindFailure => new CurrentHostNotFound(),
                _ => new UnknownNativeError("get_hostfxr_path", code),
            };
    }
}
