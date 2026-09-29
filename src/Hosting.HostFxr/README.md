# Universe.Hosting.HostFxr

A high-level C# wrapper around the .NET host, targeting **.NET 11 or later**.
Publish the consuming host as Native AOT (`<PublishAot>true</PublishAot>`).
Creating a host under JIT throws `HostFxrException`; the hosted application itself runs on CoreCLR.

```csharp
using Universe.Hosting.HostFxr;

using var host = new HostFxr(hostFxrPath);
using var context = host.InitializeForRuntimeConfig("Plugin.runtimeconfig.json").OrThrow();

var entryPoint = context.LoadAssemblyAndGetEntryPoint<int>(
    "Plugin.dll", "Plugin.EntryPoints, Plugin", "Run").OrThrow();

int exitCode = entryPoint(42);
```

The hosted method for this example is `public static int Run(nint data, int size)`.
The wrapper supplies a pointer to a copy of the `int` and its size. The method must not retain
that pointer. No unsafe code or manual buffer lengths are needed in the consuming host.

For other signatures, declare a concrete, non-generic C# delegate with the appropriate
`UnmanagedFunctionPointer` calling convention and use `LoadAssemblyAndGetDelegate<TDelegate>`.
Supply the assembly-qualified name of a matching delegate declared in the hosted assembly.
The two runtimes communicate through the native ABI: signatures must be compatible and
marshalable, and arbitrary managed object references cannot cross the boundary.

Runtime property APIs live on the context. `GetRuntimeProperties()` returns a managed snapshot;
`SetRuntimePropertyValue(name, null)` removes a property. Set properties before loading the runtime.
`LoadAssemblyBytes(ReadOnlySpan<byte>, ReadOnlySpan<byte> = default)` accepts optional symbols
and derives lengths automatically. `LoadAssembly` and `LoadAssemblyBytes` use the default load
context; `GetDelegate` and `GetEntryPoint` resolve methods from that context.

For an application, use `InitializeForDotNetCommandLine(["App.dll", "argument"])`, followed by
`context.RunApp()`. This supports application arguments, not SDK commands such as `build`.
`RunApp` preserves the native return value, which may be an application exit code or a host error.

Operations return Polyester `Result<T, TError>`. Use `TrySuccess`, `TryFailure`, C# pattern
matching with Polyester's `Success<T>` and `Failure<TError>` cases, or `OrThrow()`:

```csharp
var result = host.InitializeForRuntimeConfig("Plugin.runtimeconfig.json");
if (result.TrySuccess(out var initialized))
{
    using var context = initialized;
    Console.WriteLine(context.InitializationStatus);
}
else if (result.TryFailure(out var error))
{
    Console.Error.WriteLine(error);
}
```

Invalid managed arguments and use after disposal throw the usual .NET exceptions. Native
operation failures are returned as typed errors; unknown codes map to `Unrecoverable`.
The wrapper does not catch unrelated managed exceptions.

Every successful initialization owns a separate disposable context. `InitializationStatus`
distinguishes a new runtime, an already initialized runtime, and different runtime properties.
For the latter, inspect the runtime properties to decide whether the existing runtime is suitable.
Dispose contexts when finished. They keep hostfxr loaded even after the loader is disposed.
Closing a context does not unload CoreCLR. Entry-point wrappers reject calls after context
disposal; typed native delegates follow CoreCLR's process lifetime and do not capture a context.
Keep a context alive while binding and invoking delegates.

Migration from the initial scaffold:

- DotNext is replaced by Polyester 0.0.1-alpha.2.0. Extract successes with `TrySuccess` or
  `OrThrow`; Polyester's `Value` is the union case, not the success payload.
- Initialize contexts through `HostFxr`, rather than constructing one from a raw pointer.
- The former empty `LoadAssemblyAndGetFunctionPointer` / `GetFunctionPointer` methods are
  replaced by `LoadAssemblyAndGetEntryPoint` / `GetEntryPoint` and typed delegate methods.
- Component entry points return their exit code. Assembly-byte loading accepts spans.
- Environment/framework models use records and collections instead of native counts and
  single-element fields. Environment discovery and framework resolution are not yet exposed.

The interop layer follows the .NET runtime's
[hostfxr contract](https://github.com/dotnet/runtime/blob/main/src/native/corehost/hostfxr.h) and
[CoreCLR delegate contract](https://github.com/dotnet/runtime/blob/main/src/native/corehost/coreclr_delegates.h),
including UTF-16 strings on Windows and UTF-8 elsewhere.

## Validation

From the repository root:

```powershell
dotnet test tests/HostFxr.UnitTests/HostFxr.UnitTests.csproj
dotnet build tests/DotNetLib/DotNetLib.csproj -c Release
dotnet publish tests/HostFxr.NativeAotSmoke/HostFxr.NativeAotSmoke.csproj -c Release -r win-x64

# Use the path to your installed .NET 11 hostfxr.
$hostFxrPath = "C:/Program Files/dotnet/host/fxr/<version>/hostfxr.dll"
$smoke = "tests/HostFxr.NativeAotSmoke/bin/Release/net11.0/win-x64/publish/HostFxr.NativeAotSmoke.exe"
$component = "tests/DotNetLib/bin/Release/net11.0/DotNetLib.dll"
& $smoke $hostFxrPath $component component
& $smoke $hostFxrPath $component command
```

Native AOT publishing requires the platform's C++ toolchain. Run from a Developer PowerShell
if needed; `-p:IlcUseEnvironmentalTools=true` uses the tools already on PATH. On another
platform, use its runtime identifier, executable name, and hostfxr library path.
The component and command-line smoke tests run in separate processes because a process can
load only one CoreCLR runtime. A missing-config diagnostic in the component test is expected.

Pass `auto` instead of the hostfxr path to exercise discovery through Hosting.NetHost
before loading the runtime. Both wrappers use Hosting.Common.ErrorCodes.
