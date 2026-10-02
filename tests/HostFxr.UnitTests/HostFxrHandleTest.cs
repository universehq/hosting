using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Polyester;
using Polyester.Error;
using Universe.Hosting.Common;
using Universe.Hosting.HostFxr;
using Universe.Hosting.HostFxr.Errors;
using Host = Universe.Hosting.HostFxr.HostFxr;

namespace HostFxr.UnitTests;

public class HostFxrHandleTest
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void AllInitializationSuccessCodesAreRecognized(int code)
    {
        Assert.That(HostFxrError.IsInitializationSuccess(code), Is.True);
    }

    [TestCase(unchecked((int)0x800080a5), typeof(HostIncompatibleConfig))]
    [TestCase(unchecked((int)0x80008093), typeof(InvalidConfigFile))]
    [TestCase(unchecked((int)0x80008081), typeof(InvalidArgument))]
    [TestCase(3, typeof(UnknownNativeError))]
    public void InitializationErrorsArePreserved(int code, Type expected)
    {
        Assert.That(HostFxrError.IsInitializationSuccess(code), Is.False);
        var error = HostFxrError.MapErrorCode(code,
            new NativeErrorContext("hostfxr_initialize_for_runtime_config", "Plugin.runtimeconfig.json"));
        Assert.That(error.Value, Is.TypeOf(expected));
        Assert.That(error.ToString(), Is.Not.Empty);
    }

    [TestCase("")]
    [TestCase("a\0b")]
    public void InvalidLibraryPathsAreRejectedBeforeLoading(string path)
    {
        Assert.Throws<ArgumentException>(() => new Host(path));
    }

    [Test]
    public void HostingUnderJitIsRejected()
    {
        Assert.Throws<Universe.Hosting.HostFxr.Exceptions.HostFxrException>(() =>
            new Host("hostfxr")
        );
    }

    [TestCase("")]
    [TestCase("参数 café 😀")]
    public void NativeStringsRoundTripAndDisposeTwice(string text)
    {
        var value = new NativeString(text);
        Assert.That(NativeString.Read(value.Pointer), Is.EqualTo(text));
        value.Dispose();
        value.Dispose();
        Assert.That(value.Pointer, Is.EqualTo(nint.Zero));
    }

    [Test]
    public void NullNativeStringIsANullPointer()
    {
        using var value = new NativeString(null);
        Assert.That(value.Pointer, Is.EqualTo(nint.Zero));
        Assert.That(NativeString.Read(value.Pointer), Is.Null);
    }

    [Test]
    public void EmbeddedNullIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new NativeString("a\0b"));
    }

    [Test]
    public unsafe void EntryPointUsesUnmanagedSizeAndPreservesReturnValue()
    {
        var pointer = (nint)(delegate* unmanaged[Stdcall]<void*, int, int>)&ReadBoolean;
        // sizeof(bool) is one, whereas Marshal.SizeOf<bool>() is four.
        Assert.That(CoreClrDelegates.InvokeEntryPoint(pointer, true), Is.EqualTo(101));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int ReadBoolean(void* data, int size) => (*(bool*)data ? 100 : 0) + size;

    private static int _closeCount;

    [Test]
    public unsafe void ContextsCloseIndependentlyAndRetainTheLibrary()
    {
        var libraryName =
            OperatingSystem.IsWindows() ? "kernel32.dll"
            : OperatingSystem.IsMacOS() ? "/usr/lib/libSystem.B.dylib"
            : "libc.so.6";
        var library = new NativeLibraryHandle(libraryName);
        var close = (nint)(delegate* unmanaged[Cdecl]<nint, int>)&CloseContext;
        var firstNative = new HostContextHandle(1, close, library);
        var secondNative = new HostContextHandle(2, close, library);
        using var first = new HostFxrHandle(firstNative, InitializationStatus.Initialized);
        using var second = new HostFxrHandle(secondNative, InitializationStatus.AlreadyInitialized);
        _closeCount = 0;
        library.Dispose();

        Assert.That(library.IsClosed, Is.False);
        first.Close();
        first.Dispose();
        Assert.That(_closeCount, Is.EqualTo(1));
        Assert.That(secondNative.IsClosed, Is.False);
        Assert.That(library.IsClosed, Is.False);
        Assert.Throws<ObjectDisposedException>(() => first.GetRuntimeProperties());
        Assert.That(
            second.InitializationStatus,
            Is.EqualTo(InitializationStatus.AlreadyInitialized)
        );

        second.Dispose();
        Assert.That(_closeCount, Is.EqualTo(2));
        Assert.That(library.IsClosed, Is.True);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CloseContext(nint context)
    {
        Interlocked.Increment(ref _closeCount);
        return 0;
    }

    [TestCase(unchecked((int)0x800080a4), typeof(PropertyNotFound))]
    [TestCase(unchecked((int)0x80070002), typeof(AssemblyNotFound))]
    [TestCase(unchecked((int)0x80131513), typeof(MethodNotFound))]
    public void ContextErrorsAreTyped(int code, Type expected)
    {
        var error = HostFxrError.MapErrorCode(code,
            new NativeErrorContext("get_function_pointer", "Plugin.dll", "property", "Plugin.EntryPoints, Plugin", "Run"));
        Assert.That(error.Value, Is.TypeOf(expected));
    }

    [Test]
    public void ErrorMessagesAndPatternsRetainInputContext()
    {
        var context = new NativeErrorContext("get_function_pointer", "组件.dll", "runtime.property", "Plugin.EntryPoints, Plugin", "Run");
        var config = HostFxrError.MapErrorCode(ErrorCodes.InvalidConfigFile, context);
        var property = HostFxrError.MapErrorCode(ErrorCodes.HostPropertyNotFound, context);
        var method = HostFxrError.MapErrorCode(ErrorCodes.HResults.MissingMethod, context);

        Assert.That(config is InvalidConfigFile { RuntimeConfigPath: "组件.dll" }, Is.True);
        Assert.That(property is PropertyNotFound { PropertyName: "runtime.property" }, Is.True);
        Assert.That(method is MethodNotFound { TypeName: "Plugin.EntryPoints, Plugin", MethodName: "Run" }, Is.True);
        Assert.That(config.ToString(), Does.Contain(context.Path));
        Assert.That(property.ToString(), Does.Contain(context.PropertyName));
        Assert.That(method.ToString(), Does.Contain(context.TypeName).And.Contain(context.MethodName));
        IError reported = method;
        Assert.That(reported.ToString(), Is.EqualTo(method.ToString()));
        Assert.That(reported.Source, Is.Null);
    }

    [TestCase(-1)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(unchecked((int)0x81234567))]
    public void UnknownOperationCodesRetainNativeDiagnostics(int code)
    {
        var error = HostFxrError.MapErrorCode(code, new NativeErrorContext("get_function_pointer"));
        Assert.That(error.Value, Is.EqualTo(new UnknownNativeError("get_function_pointer", code)));
        var result = Result<int, HostFxrError>.Failure(error);
        Assert.That(result.ToString(), Does.Contain("get_function_pointer").And.Contain($"0x{code:X8}"));
        var exception = Assert.Throws<Exception>(() => result.OrThrow());
        Assert.That(exception!.Message, Does.Contain($"0x{code:X8}"));
    }

    [Test]
    public void BindingFailuresRetainTheRequestedSignature()
    {
        var context = new NativeErrorContext("get_function_pointer", TypeName: "Plugin.EntryPoints, Plugin",
            MethodName: "Run", DelegateTypeName: "Plugin.Callback, Plugin");
        var error = HostFxrError.MapErrorCode(ErrorCodes.HResults.InvalidArgument, context);
        Assert.That(error.Value, Is.EqualTo(new DelegateBindingFailure(context.Operation,
            context.TypeName, context.MethodName, context.DelegateTypeName, context.Path)));
        Assert.That(error.ToString(), Does.Contain(context.TypeName)
            .And.Contain(context.MethodName).And.Contain(context.DelegateTypeName));

        var argument = HostFxrError.MapErrorCode(ErrorCodes.HResults.InvalidArgument,
            new NativeErrorContext("load_assembly"));
        Assert.That(argument.Value, Is.TypeOf<InvalidArgument>());
    }

    [Test]
    public unsafe void MissingNativeExportsReportTheExactName()
    {
        var libraryName =
            OperatingSystem.IsWindows() ? "kernel32.dll"
            : OperatingSystem.IsMacOS() ? "/usr/lib/libSystem.B.dylib"
            : "libc.so.6";
        using var library = new NativeLibraryHandle(libraryName);
        var close = (nint)(delegate* unmanaged[Cdecl]<nint, int>)&CloseContext;
        using var context = new HostFxrHandle(new HostContextHandle(1, close, library), InitializationStatus.Initialized);

        var properties = context.GetRuntimeProperties();
        Assert.That(properties.Error is EntryPointNotFound { ExportName: "hostfxr_get_runtime_properties" }, Is.True);
        Assert.That(properties.Error.ToString(), Does.Contain("hostfxr_get_runtime_properties"));
        var value = context.GetRuntimePropertyValue("test");
        Assert.That(value.Error is EntryPointNotFound { ExportName: "hostfxr_get_runtime_property_value" }, Is.True);
        var set = context.SetRuntimePropertyValue("test", "value");
        Assert.That(set.Error is EntryPointNotFound { ExportName: "hostfxr_set_runtime_property_value" }, Is.True);
        var bytes = context.LoadAssemblyBytes([1]);
        Assert.That(bytes.Error is EntryPointNotFound { ExportName: "hostfxr_get_runtime_delegate" }, Is.True);
    }
}
