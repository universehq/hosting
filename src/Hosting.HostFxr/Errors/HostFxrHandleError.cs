using Universe.Hosting.Common;

namespace Universe.Hosting.HostFxr.Errors;

public enum HostFxrHandleError
{
    None,
    Unrecoverable,
    EntryPointNotFound,
    InvalidArgument,
    HostInvalidState,
    PropertyNotFound,
    UnsupportedVersion,
    UnsupportedScenario,
    BufferTooSmall,
    CoreClrInitFailure,
    AssemblyNotFound,
    TypeNotFound,
    MethodNotFound,
    InvalidAssembly,
}

internal static class HostFxrHandleErrorExtensions
{
    extension(HostFxrHandleError)
    {
        public static HostFxrHandleError MapErrorCode(int code) => code switch
        {
            ErrorCodes.Success => HostFxrHandleError.None,
            ErrorCodes.InvalidArgFailure or ErrorCodes.HResults.InvalidArgument => HostFxrHandleError.InvalidArgument,
            ErrorCodes.HostInvalidState => HostFxrHandleError.HostInvalidState,
            ErrorCodes.HostPropertyNotFound => HostFxrHandleError.PropertyNotFound,
            ErrorCodes.HostApiUnsupportedVersion => HostFxrHandleError.UnsupportedVersion,
            ErrorCodes.HostApiUnsupportedScenario => HostFxrHandleError.UnsupportedScenario,
            ErrorCodes.HostApiBufferTooSmall => HostFxrHandleError.BufferTooSmall,
            ErrorCodes.CoreClrInitFailure => HostFxrHandleError.CoreClrInitFailure,
            ErrorCodes.HResults.FileNotFound => HostFxrHandleError.AssemblyNotFound,
            ErrorCodes.HResults.TypeLoad => HostFxrHandleError.TypeNotFound,
            ErrorCodes.HResults.MissingMethod => HostFxrHandleError.MethodNotFound,
            ErrorCodes.HResults.BadImageFormat => HostFxrHandleError.InvalidAssembly,
            _ => HostFxrHandleError.Unrecoverable,
        };
    }
}