# Universe.Hosting.NetHost

A C# API for locating hostfxr, targeting **.NET 11 or later**, with Polyester results.
Discovery works in JIT and Native AOT applications.

```csharp
using Universe.Hosting.NetHost;

string path = NetHost.GetHostFxrPath().OrThrow();

var result = NetHost.GetHostFxrPath(new GetHostFxrParameters
{
    DotNetRoot = dotnetInstallationDirectory,
});

if (result.TrySuccess(out var hostFxrPath))
    Console.WriteLine(hostFxrPath);
else if (result.TryFailure(out var error))
    Console.Error.WriteLine(error);
```

The options are optional. AssemblyPath uses the component's location as a discovery hint;
DotNetRoot searches a specific .NET installation and takes precedence over AssemblyPath.
Null paths use native defaults. Empty paths or embedded null characters throw ArgumentException.
The native host may reuse a hostfxr library already loaded in the process.

The wrapper follows the [nethost contract](https://github.com/dotnet/runtime/blob/main/src/native/corehost/nethost/nethost.h):
Windows strings use UTF-16; Unix strings use UTF-8. Buffer resizing and native character
counts are handled internally. Native failures return typed NetHostError values; no path
is decoded from a failed call. HostFxrNotFound means discovery could not find hostfxr,
whereas NativeLibraryNotFound means nethost itself could not be loaded.

Both this project and Hosting.HostFxr reference Hosting.Common.ErrorCodes. The former
NetHost.Errors.StatusCode table has been removed. Polyester replaces DotNext: use
TrySuccess, TryFailure, or OrThrow to access a result. GetHostFxrParameters is a readonly
record struct passed by value; explicit `in` arguments should be removed.

## Native library deployment

The package and project reference copy nethost from the .NET SDK apphost pack to build
and publish outputs. RuntimeIdentifier selects the target platform and architecture;
without one, the SDK's platform is used. Publish with an explicit runtime identifier
when deploying to a different platform. Custom publish directories are supported.
For single-file applications, nethost remains alongside the executable.

- NetHostNativeLibraryPath: override the source path to nethost.dll, libnethost.so, or libnethost.dylib.
- NetHostCopyNativeLibrary=false: disable copying when deploying the native library yourself.

The matching .NET apphost pack must be available. When a build restores an apphost pack
through NuGet, its resolved path is used. Otherwise, the installed SDK pack is used.

For runtime hosting, pass the discovered path to Universe.Hosting.HostFxr.HostFxr from
a Native AOT application.

## Validation

```powershell
dotnet test tests/NetHost.UnitTests/NetHost.UnitTests.csproj
dotnet run --project tests/NetHostTests/NetHostTests.csproj
dotnet publish tests/NetHostTests/NetHostTests.csproj -c Release -r win-x64 -p:PublishAot=true
& tests/NetHostTests/bin/Release/net11.0/win-x64/publish/NetHostTests.exe
```

Use the platform's runtime identifier and executable name on Unix. Native AOT publishing
requires its C++ toolchain. The Native AOT smoke test checks missing-installation errors
before checking successful discovery.
