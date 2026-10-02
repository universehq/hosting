using Universe.Hosting.Common;

namespace Universe.Hosting.HostFxr;

public readonly record struct InitializeParameters
{
    public string? HostPath { get; init; }
    public string? DotNetRoot { get; init; }
}

/// <summary>Describes how a successful initialization relates to the process runtime.</summary>
public enum InitializationStatus
{
    Initialized = ErrorCodes.Success,
    AlreadyInitialized = ErrorCodes.SuccessHostAlreadyInitialized,
    DifferentRuntimeProperties = ErrorCodes.SuccessDifferentRuntimeProperties,
}

public sealed record DotnetEnvironmentSdkInfo(string Version, string Path);

public sealed record DotnetEnvironmentFrameworkInfo(string Name, string Version, string Path);

public sealed record DotnetEnvironmentInfo(
    string HostFxrVersion,
    string HostFxrCommitHash,
    IReadOnlyList<DotnetEnvironmentSdkInfo> Sdks,
    IReadOnlyList<DotnetEnvironmentFrameworkInfo> Frameworks
);

public sealed record FrameworkResult(
    string Name,
    string RequestedVersion,
    string? ResolvedVersion,
    string? ResolvedPath
);

public sealed record ResolveFrameworksResult(
    IReadOnlyList<FrameworkResult> ResolvedFrameworks,
    IReadOnlyList<FrameworkResult> UnresolvedFrameworks
);
