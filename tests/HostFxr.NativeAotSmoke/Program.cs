using System.Runtime.InteropServices;
using System.Text.Json;
using Universe.Hosting.HostFxr;
using Universe.Hosting.HostFxr.Errors;
using Universe.Hosting.NetHost;

if (args.Length != 3)
    throw new ArgumentException(
        "Usage: HostFxr.NativeAotSmoke <hostfxr path|auto> <DotNetLib.dll path> <component|command>"
    );

var assemblyPath = Path.GetFullPath(args[1]);
var configPath = Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");
var missingRoot = Path.Combine(Path.GetTempPath(), $"missing-dotnet-{Guid.NewGuid():N}");
var missingHost = NetHost.GetHostFxrPath(new GetHostFxrParameters { DotNetRoot = missingRoot });
Check(missingHost.Error is Universe.Hosting.NetHost.Errors.HostFxrNotFound { DotNetRoot: var reportedRoot }
    && reportedRoot == missingRoot, "discovery failure retains installation hint");
var hostFxrPath = args[0] == "auto" ? NetHost.GetHostFxrPath().OrThrow() : args[0];
var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(hostFxrPath)!, "../../.."));
using var host = new HostFxr(hostFxrPath);

if (args[2] == "command")
{
    using var app = host.InitializeForDotNetCommandLine(
            [assemblyPath, "参数 café 😀", "", "two words"],
            new InitializeParameters { DotNetRoot = root }
        )
        .OrThrow();
    Check(app.RunApp() == 37, "command arguments and exit code");
    Console.WriteLine("PASS: command-line hosting");
    return;
}
if (args[2] != "component")
    throw new ArgumentException("Unknown smoke-test mode.");

var nativeLibrary =
    OperatingSystem.IsWindows() ? "kernel32.dll"
    : OperatingSystem.IsMacOS() ? "/usr/lib/libSystem.B.dylib"
    : "libc.so.6";
using (var incompatibleHost = new HostFxr(nativeLibrary))
{
    Check(incompatibleHost.InitializeForRuntimeConfig(configPath).Error
        is EntryPointNotFound { ExportName: "hostfxr_close" }, "missing initialization export");
}

Check(
    host.InitializeForRuntimeConfig(configPath + ".missing").Error
        is InvalidConfigFile { RuntimeConfigPath: var missingPath } && missingPath == configPath + ".missing",
    "missing config returns a Polyester failure"
);
try
{
    host.InitializeForDotNetCommandLine(["app.dll", "bad\0argument"]);
    throw new Exception("Embedded null was accepted.");
}
catch (ArgumentException) { }

using var context = host.InitializeForRuntimeConfig(
        configPath,
        new InitializeParameters { HostPath = Environment.ProcessPath }
    )
    .OrThrow();
Check(context.InitializationStatus == InitializationStatus.Initialized, "first initialization");
context.SetRuntimePropertyValue("hosting.test", "值 café 😀").OrThrow();
Check(
    context.GetRuntimePropertyValue("hosting.test").OrThrow() == "值 café 😀",
    "Unicode property"
);
var snapshot = context.GetRuntimeProperties().OrThrow();
Check(snapshot["hosting.test"] == "值 café 😀", "property snapshot");
context.SetRuntimePropertyValue("hosting.test", null).OrThrow();
Check(
    context.GetRuntimePropertyValue("hosting.test").Error
        is PropertyNotFound { PropertyName: "hosting.test" },
    "remove property"
);
Check(snapshot["hosting.test"] == "值 café 😀", "snapshot owns its strings");

var entryPoint = context
    .LoadAssemblyAndGetEntryPoint<int>(assemblyPath, "DotNetLib.Lib, DotNetLib", "EntryPoint")
    .OrThrow();
Check(entryPoint(41) == 42, "component entry point");
var add = context
    .LoadAssemblyAndGetDelegate<AddDelegate>(
        assemblyPath,
        "DotNetLib.Lib, DotNetLib",
        "Add",
        "DotNetLib.AddDelegate, DotNetLib"
    )
    .OrThrow();
Check(add(20, 22) == 42, "typed managed delegate");
var missingMethod = context.LoadAssemblyAndGetEntryPoint<int>(
    assemblyPath, "DotNetLib.Lib, DotNetLib", "Missing");
Check(missingMethod.Error is DelegateBindingFailure
    { TypeName: "DotNetLib.Lib, DotNetLib", MethodName: "Missing", DelegateTypeName: "component entry point" },
    $"missing method context: {missingMethod.Error}");
Check(missingMethod.Error.ToString().Contains("Missing"), "missing method message");

var missingType = context.LoadAssemblyAndGetEntryPoint<int>(
    assemblyPath, "DotNetLib.Missing, DotNetLib", "EntryPoint");
Check(missingType.Error is TypeNotFound { TypeName: "DotNetLib.Missing, DotNetLib" }, "missing type context");

// Use the default load context for GetDelegate/GetEntryPoint, including loading from spans.
context.LoadAssemblyBytes(File.ReadAllBytes(assemblyPath)).OrThrow();
context.LoadAssembly(assemblyPath).OrThrow();
var missingAssemblyPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, "Missing.dll");
var missingAssemblyResult = context.LoadAssembly(missingAssemblyPath);
Check(missingAssemblyResult.Error
    is UnknownNativeError { Operation: "load_assembly", Code: unchecked((int)0x80131509), InputPath: var missingAssembly }
        && missingAssembly == missingAssemblyPath,
    $"unclassified assembly failure retains native code and input: {missingAssemblyResult.Error}");
Check(context.LoadAssemblyBytes([1, 2, 3]).Error is InvalidAssembly { Assembly: "assembly bytes" },
    "invalid assembly bytes");
var loadedAdd = context
    .GetDelegate<AddDelegate>("DotNetLib.Lib, DotNetLib", "Add", "DotNetLib.AddDelegate, DotNetLib")
    .OrThrow();
Check(loadedAdd(1, 2) == 3, "delegate from default context");
Check(
    context.GetEntryPoint<int>("DotNetLib.Lib, DotNetLib", "EntryPoint").OrThrow()(9) == 10,
    "entry point from default context"
);

using var second = host.InitializeForRuntimeConfig(
        configPath,
        new InitializeParameters { DotNetRoot = root }
    )
    .OrThrow();
Check(
    second.InitializationStatus == InitializationStatus.AlreadyInitialized,
    "repeat initialization status"
);
Check(!ReferenceEquals(context, second), "distinct owned contexts");

// hostfxr's third success status must also carry a usable independent context.
var changedConfig = Path.Combine(
    Path.GetTempPath(),
    $"hosting-{Guid.NewGuid():N}.runtimeconfig.json"
);
try
{
    using var original = JsonDocument.Parse(File.ReadAllText(configPath));
    var options = original.RootElement.GetProperty("runtimeOptions");
    // Runtime configuration files in the fixture are generated by the SDK.
    var changed = options.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    changed["configProperties"] = JsonSerializer.SerializeToElement(
        new Dictionary<string, string> { ["hosting.different"] = "yes" },
        SmokeJsonContext.Default.DictionaryStringString
    );
    var config = new Dictionary<string, Dictionary<string, JsonElement>>
    {
        ["runtimeOptions"] = changed,
    };
    File.WriteAllText(
        changedConfig,
        JsonSerializer.Serialize(
            config,
            SmokeJsonContext.Default.DictionaryStringDictionaryStringJsonElement
        )
    );
    using var third = host.InitializeForRuntimeConfig(changedConfig).OrThrow();
    Check(
        third.InitializationStatus == InitializationStatus.DifferentRuntimeProperties,
        "different properties status"
    );
}
finally
{
    File.Delete(changedConfig);
}

second.Close();
second.Dispose();
host.Dispose();
Check(entryPoint(1) == 2, "context outlives loader");
Check(context.GetRuntimeProperties().IsSuccessful, "library remains loaded");
try
{
    host.InitializeForRuntimeConfig(configPath);
    throw new Exception("Disposed host was accepted.");
}
catch (ObjectDisposedException) { }
context.Dispose();
try
{
    entryPoint(1);
    throw new Exception("Disposed context was accepted.");
}
catch (ObjectDisposedException) { }
Console.WriteLine("PASS: component hosting, properties, delegates, spans, statuses, and disposal");

static void Check(bool condition, string description)
{
    if (!condition)
        throw new Exception($"Failed: {description}");
}

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int AddDelegate(int left, int right);

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
[System.Text.Json.Serialization.JsonSerializable(
    typeof(Dictionary<string, Dictionary<string, JsonElement>>)
)]
internal partial class SmokeJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
