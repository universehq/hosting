using Failure.CompilerServices;
using Universe.Hosting.Common;

namespace Universe.Hosting.HostFxr.Errors;

/// <summary>Contextual failures returned by hostfxr initialization and runtime operations.</summary>
[FailureImpl]
public readonly partial union HostFxrError(
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
    PropertyNotFound,
    UnsupportedVersion,
    BufferTooSmall,
    AssemblyNotFound,
    TypeNotFound,
    MethodNotFound,
    InvalidAssembly,
    DelegateBindingFailure,
    UnknownNativeError
);

[Failure("Required hostfxr export '{ExportName}' was not found.")]
public readonly record struct EntryPointNotFound(string ExportName);

[Failure("Runtime configuration '{RuntimeConfigPath}' is invalid.")]
public readonly record struct InvalidConfigFile(string RuntimeConfigPath);

[Failure("Framework resolution for '{Path}' requires a compatibility retry.")]
public readonly record struct FrameworkCompatRetry(string Path);

[Failure("The frameworks required by '{Path}' are incompatible.")]
public readonly record struct FrameworkCompatFailure(string Path);

[Failure("The application host '{Path}' is not bound to a managed application.")]
public readonly record struct AppHostExeNotBoundFailure(string Path);

[Failure("A framework required by '{Path}' could not be found.")]
public readonly record struct FrameworkMissingFailure(string Path);

[Failure("CoreCLR initialization failed during '{Operation}'.")]
public readonly record struct CoreClrInitFailure(string Operation);

[Failure("CoreCLR could not be bound for '{Path}'.")]
public readonly record struct CoreClrBindFailure(string Path);

[Failure("CoreCLR could not be resolved for '{Path}'.")]
public readonly record struct CoreClrResolveFailure(string Path);

[Failure("The current host could not be found for '{Path}'.")]
public readonly record struct CoreHostCurHostFindFailure(string Path);

[Failure("A required native host entry point could not be found for '{Path}'.")]
public readonly record struct CoreHostEntryPointFailure(string Path);

[Failure("A native host library could not be loaded for '{Path}'.")]
public readonly record struct CoreHostLibLoadFailure(string Path);

[Failure("A native host library required by '{Path}' could not be found.")]
public readonly record struct CoreHostLibMissingFailure(string Path);

[Failure("The host state does not allow '{Operation}'.")]
public readonly record struct HostInvalidState(string Operation);

[Failure("Native operation '{Operation}' received an invalid argument.")]
public readonly record struct InvalidArgument(string Operation);

[Failure("The configuration for '{Path}' is incompatible with the initialized host.")]
public readonly record struct HostIncompatibleConfig(string Path);

[Failure("Application '{Path}' cannot be run by this host.")]
public readonly record struct AppNotRunnable(string Path);

[Failure("Native operation '{Operation}' is unsupported in this hosting scenario.")]
public readonly record struct UnsupportedScenario(string Operation);

[Failure("Runtime property '{PropertyName}' was not found.")]
public readonly record struct PropertyNotFound(string PropertyName);

[Failure("Native operation '{Operation}' requires an unsupported API version.")]
public readonly record struct UnsupportedVersion(string Operation);

[Failure("The native buffer for '{Operation}' is too small.")]
public readonly record struct BufferTooSmall(string Operation);

[Failure("Assembly '{Assembly}' could not be found.")]
public readonly record struct AssemblyNotFound(string Assembly);

[Failure("Type '{TypeName}' could not be found.")]
public readonly record struct TypeNotFound(string TypeName);

[Failure("Method '{MethodName}' on type '{TypeName}' could not be found.")]
public readonly record struct MethodNotFound(string TypeName, string MethodName);

[Failure("Assembly '{Assembly}' has an invalid format.")]
public readonly record struct InvalidAssembly(string Assembly);

[Failure("Method '{MethodName}' on type '{TypeName}' could not be bound to '{DelegateTypeName}' during '{Operation}'.")]
public readonly record struct DelegateBindingFailure(
    string Operation, string TypeName, string MethodName, string DelegateTypeName, string AssemblyPath);

[Failure("'{Operation}' failed with native code 0x{Code:X8}.")]
public readonly record struct UnknownNativeError(string Operation, int Code, string? InputPath = null);

internal readonly record struct NativeErrorContext(
    string Operation,
    string Path = "",
    string PropertyName = "",
    string TypeName = "",
    string MethodName = "",
    string DelegateTypeName = ""
);

internal static class HostFxrErrorExtensions
{
    extension(HostFxrError)
    {
        public static bool IsInitializationSuccess(int code) =>
            code is ErrorCodes.Success
                or ErrorCodes.SuccessHostAlreadyInitialized
                or ErrorCodes.SuccessDifferentRuntimeProperties;

        // Success codes are handled by the operation before mapping a failure.
        public static HostFxrError MapErrorCode(int code, NativeErrorContext context) =>
            code switch
            {
                ErrorCodes.HResults.InvalidArgument when context.MethodName.Length > 0 =>
                    new DelegateBindingFailure(context.Operation, context.TypeName, context.MethodName,
                        context.DelegateTypeName, context.Path),
                ErrorCodes.InvalidArgFailure or ErrorCodes.HResults.InvalidArgument =>
                    new InvalidArgument(context.Operation),
                ErrorCodes.InvalidConfigFile => new InvalidConfigFile(context.Path),
                ErrorCodes.FrameworkCompatRetry => new FrameworkCompatRetry(context.Path),
                ErrorCodes.FrameworkCompatFailure => new FrameworkCompatFailure(context.Path),
                ErrorCodes.AppHostExeNotBoundFailure => new AppHostExeNotBoundFailure(context.Path),
                ErrorCodes.FrameworkMissingFailure => new FrameworkMissingFailure(context.Path),
                ErrorCodes.CoreClrInitFailure => new CoreClrInitFailure(context.Operation),
                ErrorCodes.CoreClrBindFailure => new CoreClrBindFailure(context.Path),
                ErrorCodes.CoreClrResolveFailure => new CoreClrResolveFailure(context.Path),
                ErrorCodes.CoreHostCurHostFindFailure => new CoreHostCurHostFindFailure(context.Path),
                ErrorCodes.CoreHostEntryPointFailure => new CoreHostEntryPointFailure(context.Path),
                ErrorCodes.CoreHostLibLoadFailure => new CoreHostLibLoadFailure(context.Path),
                ErrorCodes.CoreHostLibMissingFailure => new CoreHostLibMissingFailure(context.Path),
                ErrorCodes.HostInvalidState => new HostInvalidState(context.Operation),
                ErrorCodes.HostIncompatibleConfig => new HostIncompatibleConfig(context.Path),
                ErrorCodes.AppArgNotRunnable => new AppNotRunnable(context.Path),
                ErrorCodes.HostApiUnsupportedScenario => new UnsupportedScenario(context.Operation),
                ErrorCodes.HostPropertyNotFound => new PropertyNotFound(context.PropertyName),
                ErrorCodes.HostApiUnsupportedVersion => new UnsupportedVersion(context.Operation),
                ErrorCodes.HostApiBufferTooSmall => new BufferTooSmall(context.Operation),
                ErrorCodes.HResults.FileNotFound =>
                    new AssemblyNotFound(context.Path.Length > 0 ? context.Path : context.TypeName),
                ErrorCodes.HResults.TypeLoad => new TypeNotFound(context.TypeName),
                ErrorCodes.HResults.MissingMethod => new MethodNotFound(context.TypeName, context.MethodName),
                ErrorCodes.HResults.BadImageFormat => new InvalidAssembly(context.Path),
                _ => new UnknownNativeError(context.Operation, code,
                    context.Path.Length == 0 ? null : context.Path),
            };
    }
}
