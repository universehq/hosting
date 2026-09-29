using Polyester;
using Universe.Hosting.NetHost.Errors;

namespace Universe.Hosting.NetHost;

/// <summary>Discovers the hostfxr library using .NET's native hosting search rules.</summary>
public static class NetHost
{
    /// <summary>Gets the hostfxr path using optional assembly or .NET installation hints.</summary>
    /// <remarks>Discovery works in both JIT and Native AOT applications and does not load CoreCLR.
    /// A supplied DotNetRoot takes precedence over AssemblyPath.</remarks>
    public static unsafe Result<string, NetHostError> GetHostFxrPath(
        GetHostFxrParameters? parameters = null
    )
    {
        ValidatePath(parameters?.AssemblyPath, nameof(GetHostFxrParameters.AssemblyPath));
        ValidatePath(parameters?.DotNetRoot, nameof(GetHostFxrParameters.DotNetRoot));
        return NativeMethods.ResolvePath(parameters, NativeMethods.GetHostFxrPath);
    }

    private static void ValidatePath(string? path, string parameterName)
    {
        if (path is not null && (path.Length == 0 || path.Contains('\0')))
            throw new ArgumentException(
                "A path must be nonempty and contain no null characters.",
                parameterName
            );
    }
}

/// <summary>Optional hints for locating hostfxr.</summary>
public readonly record struct GetHostFxrParameters
{
    /// <summary>Locates hostfxr as if this component assembly were an apphost.</summary>
    public string? AssemblyPath { get; init; }

    /// <summary>The directory containing the dotnet executable. Overrides AssemblyPath when supplied.</summary>
    public string? DotNetRoot { get; init; }
}
