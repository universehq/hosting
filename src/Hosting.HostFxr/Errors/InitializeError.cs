using Universe.Hosting.Common;

namespace Universe.Hosting.HostFxr.Errors;

public enum InitializeError
{
    None,
    Unrecoverable,
    EntryPointNotFound,
    InvalidConfigFile,
    FrameworkCompatRetry,
    FrameworkCompatFailure,
    AppHostExeNotBoundFailure,
    FrameworkMissingFailure,
    CoreClrInitFailure,
    CoreClrBindFailure,
    CoreClrResolveFailure,
    CoreHostCurHostFindFailure,
    CoreHostEntryPointFailure,
    CoreHostLibLoadFailure,
    CoreHostLibMissingFailure,
    HostInvalidState,
    InvalidArgument,
    HostIncompatibleConfig,
    AppNotRunnable,
    UnsupportedScenario,
}

// Search error codes of hostfxr hostfxr_initialize_for_dotnet_command_line_fn
internal static class InitializeErrorExtensions
{
    extension(InitializeError error)
    {
        public static InitializeError MapErrorCode(in int code) =>
            code switch
            {
                ErrorCodes.Success
                or ErrorCodes.SuccessHostAlreadyInitialized
                or ErrorCodes.SuccessDifferentRuntimeProperties => InitializeError.None,

                ErrorCodes.InvalidArgFailure => InitializeError.InvalidArgument,
                ErrorCodes.HostIncompatibleConfig => InitializeError.HostIncompatibleConfig,
                ErrorCodes.AppArgNotRunnable => InitializeError.AppNotRunnable,
                ErrorCodes.HostApiUnsupportedScenario => InitializeError.UnsupportedScenario,

                ErrorCodes.InvalidConfigFile => InitializeError.InvalidConfigFile,

                ErrorCodes.FrameworkCompatRetry => InitializeError.FrameworkCompatRetry,

                ErrorCodes.FrameworkCompatFailure => InitializeError.FrameworkCompatFailure,

                ErrorCodes.AppHostExeNotBoundFailure => InitializeError.AppHostExeNotBoundFailure,

                ErrorCodes.FrameworkMissingFailure => InitializeError.FrameworkMissingFailure,

                ErrorCodes.CoreClrInitFailure => InitializeError.CoreClrInitFailure,

                ErrorCodes.CoreClrBindFailure => InitializeError.CoreClrBindFailure,

                ErrorCodes.CoreClrResolveFailure => InitializeError.CoreClrResolveFailure,

                ErrorCodes.CoreHostCurHostFindFailure => InitializeError.CoreHostCurHostFindFailure,

                ErrorCodes.CoreHostEntryPointFailure => InitializeError.CoreHostEntryPointFailure,

                ErrorCodes.CoreHostLibLoadFailure => InitializeError.CoreHostLibLoadFailure,

                ErrorCodes.CoreHostLibMissingFailure => InitializeError.CoreHostLibMissingFailure,

                ErrorCodes.HostInvalidState => InitializeError.HostInvalidState,

                _ => InitializeError.Unrecoverable,
            };
    }
}
