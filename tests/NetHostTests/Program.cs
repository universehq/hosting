using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Universe.Hosting.NetHost;
using Universe.Hosting.NetHost.Errors;

// Run discovery before loading hostfxr. Native AOT also exercises missing-installation lookup
// without a JIT host that may have already loaded hostfxr into this process.
if (!RuntimeFeature.IsDynamicCodeSupported)
{
    var missing = NetHost.GetHostFxrPath(
        new GetHostFxrParameters
        {
            DotNetRoot = Path.Combine(Path.GetTempPath(), $"missing-dotnet-{Guid.NewGuid():N}"),
        }
    );
    if (missing.Error is not HostFxrNotFound)
        throw new Exception($"Expected HostFxrNotFound, got {missing}.");
}

var path = NetHost.GetHostFxrPath().OrThrow();
if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
    throw new Exception($"Discovery returned an invalid path: {path}");

var root =
    args.Length > 0
        ? Path.GetFullPath(args[0])
        : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "../../.."));
var explicitPath = NetHost
    .GetHostFxrPath(
        new GetHostFxrParameters
        {
            DotNetRoot = root,
            AssemblyPath = Path.Combine(AppContext.BaseDirectory, "组件 café 😀.dll"),
        }
    )
    .OrThrow();
if (!File.Exists(explicitPath))
    throw new Exception($"Explicit discovery returned an invalid path: {explicitPath}");

var handle = NativeLibrary.Load(explicitPath);
try
{
    if (!NativeLibrary.TryGetExport(handle, "hostfxr_initialize_for_runtime_config", out _))
        throw new Exception("The discovered library is missing a hostfxr export.");
}
finally
{
    NativeLibrary.Free(handle);
}
Console.WriteLine(
    $"PASS: {(RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "Native AOT")} nethost discovery: {explicitPath}"
);
