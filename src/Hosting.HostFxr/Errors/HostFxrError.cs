using Failure.CompilerServices;

namespace Universe.Hosting.HostFxr.Errors;

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
    UnsupportedScenario
);

[Failure("Required hostfxr export '{ExportName}' was not found.")]
public readonly record struct EntryPointNotFound(string ExportName);

[Failure("Runtime configuration '{RuntimeConfigPath}' is invalid.")]
public readonly record struct InvalidConfigFile(string RuntimeConfigPath);

[Failure("")]
public readonly record struct FrameworkCompatRetry
{
    public readonly required string Path { get; init; }
}

[Failure("")]
public readonly record struct FrameworkCompatFailure
{
    public readonly required string Path { get; init; }
}

[Failure("")]
public readonly record struct AppHostExeNotBoundFailure
{
    public readonly required string Path { get; init; }
}

[Failure("")]
public readonly record struct FrameworkMissingFailure
{
    public readonly required string Path { get; init; }
}

[Failure("")]
public readonly record struct CoreClrInitFailure
{
    public readonly required string Path { get; init; }
}

[Failure("")]
public readonly record struct CoreClrBindFailure
{
    public readonly required string Path { get; init; }
}

[Failure("")]
public readonly record struct CoreClrResolveFailure
{
    public readonly required string Path { get; init; }
}

[Failure("")]
public readonly record struct CoreHostCurHostFindFailure
{
    public readonly required string Path { get; init; }
}


[Failure("")]
public readonly record struct CoreHostEntryPointFailure
{
    public readonly required string Path { get; init; }
}

[Failure("")]
public readonly record struct CoreHostLibLoadFailure
{
    public readonly required string Path { get; init; }
}

[Failure("")]
public readonly record struct CoreHostLibMissingFailure
{
    public readonly required string Path { get; init; }
}

[Failure("The host state does not allow '{Operation}'.")]
public readonly record struct HostInvalidState(string Operation);

[Failure("")]
public readonly record struct InvalidArgument
{
    public readonly required string Path { get; init; }
}

[Failure("")]
public readonly record struct HostIncompatibleConfig
{
    public readonly required string Path { get; init; }
}

[Failure("")]
public readonly record struct AppNotRunnable
{
    public readonly required string Path { get; init; }
}

[Failure("")]
public readonly record struct UnsupportedScenario
{
    public readonly required string Path { get; init; }
}

[Failure("'{Operation}' failed with native code 0x{Code:X8}.")]
public readonly record struct UnknownNativeError(string Operation, int Code);
