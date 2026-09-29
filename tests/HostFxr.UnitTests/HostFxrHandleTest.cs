using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
        Assert.That(InitializeError.MapErrorCode(code), Is.EqualTo(InitializeError.None));
    }

    [TestCase(unchecked((int)0x800080a5), InitializeError.HostIncompatibleConfig)]
    [TestCase(unchecked((int)0x80008093), InitializeError.InvalidConfigFile)]
    [TestCase(unchecked((int)0x80008081), InitializeError.InvalidArgument)]
    [TestCase(3, InitializeError.Unrecoverable)]
    public void InitializationErrorsArePreserved(int code, InitializeError expected)
    {
        Assert.That(InitializeError.MapErrorCode(code), Is.EqualTo(expected));
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

    [TestCase(unchecked((int)0x800080a4), HostFxrHandleError.PropertyNotFound)]
    [TestCase(unchecked((int)0x80070002), HostFxrHandleError.AssemblyNotFound)]
    [TestCase(unchecked((int)0x80131513), HostFxrHandleError.MethodNotFound)]
    public void ContextErrorsAreTyped(int code, HostFxrHandleError expected)
    {
        Assert.That(HostFxrHandleError.MapErrorCode(code), Is.EqualTo(expected));
    }
}
