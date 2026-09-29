using Universe.Hosting.Common;

namespace Universe.Hosting.NetHost.Errors;

/// <summary>Failures encountered while discovering the .NET host.</summary>
public enum NetHostError
{
    None,
    Unrecoverable,
    InvalidArgFailure,
    HostFxrNotFound,
    HostFxrLoadFailure,
    HostFxrEntryPointNotFound,
    CurrentHostNotFound,
    BufferTooSmall,
    NativeLibraryNotFound,
    NativeEntryPointNotFound,
    NativeLibraryIncompatible,
}

internal static class NetHostErrorExtensions
{
    extension(NetHostError)
    {
        public static NetHostError MapErrorCode(int code) =>
            code switch
            {
                ErrorCodes.Success => NetHostError.None,
                ErrorCodes.InvalidArgFailure => NetHostError.InvalidArgFailure,
                ErrorCodes.CoreHostLibMissingFailure => NetHostError.HostFxrNotFound,
                ErrorCodes.CoreHostLibLoadFailure => NetHostError.HostFxrLoadFailure,
                ErrorCodes.CoreHostEntryPointFailure => NetHostError.HostFxrEntryPointNotFound,
                ErrorCodes.CoreHostCurHostFindFailure => NetHostError.CurrentHostNotFound,
                ErrorCodes.HostApiBufferTooSmall => NetHostError.BufferTooSmall,
                _ => NetHostError.Unrecoverable,
            };
    }
}
