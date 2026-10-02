using System.Runtime.InteropServices;
using System.Text;
using Universe.Hosting.Common;
using Universe.Hosting.NetHost;
using Universe.Hosting.NetHost.Errors;
using HostingNetHost = Universe.Hosting.NetHost.NetHost;

namespace NetHost.UnitTests;

public unsafe class NetHostTests
{
    [Test]
    public void DefaultDiscoveryUsesNullParametersAndDecodesUnicode()
    {
        const string path = "路径 café 😀/hostfxr";
        var result = NativeMethods.ResolvePath(
            null,
            (buffer, length, parameters) =>
            {
                Assert.That(parameters == null, Is.True);
                return WritePath(buffer, length, path);
            }
        );
        Assert.That(result.OrThrow(), Is.EqualTo(path));
    }

    [TestCase(null, null)]
    [TestCase("组件 😀.dll", null)]
    [TestCase(null, "dotnet 路径")]
    [TestCase("组件 😀.dll", "dotnet 路径")]
    public void OptionalParametersAreForwardedOnEveryRetry(string? assembly, string? root)
    {
        var calls = 0;
        var path = new string('x', 1700) + "/hostfxr";
        var result = NativeMethods.ResolvePath(
            new GetHostFxrParameters { AssemblyPath = assembly, DotNetRoot = root },
            (buffer, length, parameters) =>
            {
                Assert.That(parameters != null, Is.True);
                Assert.That(parameters->Size, Is.EqualTo((nuint)sizeof(NativeMethods.Parameters)));
                Assert.That(ReadString(parameters->AssemblyPath), Is.EqualTo(assembly));
                Assert.That(ReadString(parameters->DotNetRoot), Is.EqualTo(root));
                calls++;
                if (calls == 1)
                {
                    *length = 1000;
                    return ErrorCodes.HostApiBufferTooSmall;
                }
                return WritePath(buffer, length, path);
            }
        );
        Assert.That(calls, Is.EqualTo(3));
        Assert.That(result.OrThrow(), Is.EqualTo(path));
    }

    [TestCase(ErrorCodes.InvalidArgFailure, typeof(InvalidArgFailure))]
    [TestCase(ErrorCodes.CoreHostLibMissingFailure, typeof(HostFxrNotFound))]
    [TestCase(ErrorCodes.CoreHostLibLoadFailure, typeof(HostFxrLoadFailure))]
    [TestCase(ErrorCodes.CoreHostEntryPointFailure, typeof(HostFxrEntryPointNotFound))]
    [TestCase(ErrorCodes.CoreHostCurHostFindFailure, typeof(CurrentHostNotFound))]
    [TestCase(-1, typeof(UnknownNativeError))]
    public void NativeFailuresAreReturnedWithoutDecodingTheBuffer(int code, Type expected)
    {
        var result = NativeMethods.ResolvePath(
            null,
            (buffer, length, parameters) =>
            {
                *length = 0;
                return code;
            }
        );
        Assert.That(result.TryFailure(out var error), Is.True);
        Assert.That(error.Value, Is.TypeOf(expected));
        Assert.That(error.ToString(), Is.Not.Empty);
    }

    [Test]
    public void FailureAfterResizeIsReturned()
    {
        var calls = 0;
        var result = NativeMethods.ResolvePath(
            null,
            (buffer, length, parameters) =>
            {
                if (++calls == 1)
                {
                    *length = 2048;
                    return ErrorCodes.HostApiBufferTooSmall;
                }
                *length = 0;
                return ErrorCodes.CoreHostLibMissingFailure;
            }
        );
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(result.Error.Value, Is.TypeOf<HostFxrNotFound>());
    }

    [Test]
    public void RepeatedGrowthHasABoundedRetryCount()
    {
        var calls = 0;
        var result = NativeMethods.ResolvePath(
            null,
            (buffer, length, parameters) =>
            {
                calls++;
                *length *= 2;
                return ErrorCodes.HostApiBufferTooSmall;
            }
        );
        Assert.That(calls, Is.EqualTo(3));
        Assert.That(result.Error.Value, Is.EqualTo(new BufferTooSmall(2048, 4096)));
    }

    [Test]
    public void ImpossibleBufferLengthDoesNotOverflowOrAllocate()
    {
        var calls = 0;
        var result = NativeMethods.ResolvePath(
            null,
            (buffer, length, parameters) =>
            {
                calls++;
                *length = nuint.MaxValue;
                return ErrorCodes.HostApiBufferTooSmall;
            }
        );
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(result.Error.Value, Is.EqualTo(new InvalidNativeResponse(
            "requested buffer exceeds managed array limits", 512, nuint.MaxValue)));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(513)]
    public void InvalidSuccessLengthsAreRejected(int size)
    {
        var result = NativeMethods.ResolvePath(
            null,
            (buffer, length, parameters) =>
            {
                *length = (nuint)size;
                return ErrorCodes.Success;
            }
        );
        Assert.That(result.Error.Value, Is.TypeOf<InvalidNativeResponse>());
        var error = (InvalidNativeResponse)result.Error.Value!;
        Assert.That(error.Capacity, Is.EqualTo((nuint)512));
        Assert.That(error.Length, Is.EqualTo((nuint)size));
    }

    [Test]
    public void MissingTerminatorIsRejected()
    {
        var result = NativeMethods.ResolvePath(
            null,
            (buffer, length, parameters) =>
            {
                new Span<byte>(buffer, 8).Fill(65);
                *length = 4;
                return ErrorCodes.Success;
            }
        );
        Assert.That(result.Error.Value, Is.TypeOf<InvalidNativeResponse>());
        Assert.That(result.Error.ToString(), Does.Contain("not null-terminated"));
    }

    [TestCase(typeof(DllNotFoundException), typeof(NativeLibraryNotFound))]
    [TestCase(typeof(EntryPointNotFoundException), typeof(NativeEntryPointNotFound))]
    [TestCase(typeof(BadImageFormatException), typeof(NativeLibraryIncompatible))]
    public void NativeLoadingFailuresHaveDistinctErrors(Type exceptionType, Type expected)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "native loader diagnostic")!;
        var result = NativeMethods.ResolvePath(
            null,
            (buffer, length, parameters) => throw exception
        );
        Assert.That(result.Error.Value, Is.TypeOf(expected));
        var cause = result.Error switch
        {
            NativeLibraryNotFound error => error.Exception,
            NativeEntryPointNotFound error => error.Exception,
            NativeLibraryIncompatible error => (Exception)error.Exception,
            _ => null,
        };
        Assert.That(cause, Is.SameAs(exception));
        Assert.That(result.Error.ToString(), Does.Contain("nethost"));
    }

    [Test]
    public void DiscoveryFailureRetainsBothSearchHints()
    {
        var options = new GetHostFxrParameters { DotNetRoot = "dotnet 路径", AssemblyPath = "组件.dll" };
        var result = NativeMethods.ResolvePath(options,
            (buffer, length, parameters) => ErrorCodes.CoreHostLibMissingFailure);

        Assert.That(result.Error.Value, Is.EqualTo(new HostFxrNotFound(options.DotNetRoot, options.AssemblyPath)));
        Assert.That(result.Error.ToString(), Does.Contain(options.DotNetRoot).And.Contain(options.AssemblyPath));
    }

    [TestCase(-1)]
    [TestCase(1)]
    [TestCase(unchecked((int)0x81234567))]
    public void UnknownNativeCodesRetainOperationAndCode(int code)
    {
        var result = NativeMethods.ResolvePath(null, (buffer, length, parameters) => code);
        Assert.That(result.Error.Value, Is.EqualTo(new UnknownNativeError("get_hostfxr_path", code)));
        Assert.That(result.ToString(), Does.Contain($"0x{code:X8}"));
        var exception = Assert.Throws<Exception>(() => result.OrThrow());
        Assert.That(exception!.Message, Does.Contain("get_hostfxr_path").And.Contain($"0x{code:X8}"));
    }

    [TestCase(0)]
    [TestCase(512)]
    public void BufferTooSmallWithoutGrowthRetainsReportedSizes(int size)
    {
        var result = NativeMethods.ResolvePath(null, (buffer, length, parameters) =>
        {
            *length = (nuint)size;
            return ErrorCodes.HostApiBufferTooSmall;
        });
        Assert.That(result.Error.Value, Is.EqualTo(new BufferTooSmall(512, (nuint)size)));
    }

    [Test]
    public void UnexpectedManagedExceptionsPropagate()
    {
        Assert.Throws<InvalidOperationException>(() =>
            NativeMethods.ResolvePath(
                null,
                (buffer, length, parameters) => throw new InvalidOperationException()
            )
        );
    }

    [TestCase("")]
    [TestCase("bad\0path")]
    public void InvalidOptionsThrowBeforeCallingNativeCode(string path)
    {
        Assert.Throws<ArgumentException>(() =>
            HostingNetHost.GetHostFxrPath(new GetHostFxrParameters { AssemblyPath = path })
        );
        Assert.Throws<ArgumentException>(() =>
            HostingNetHost.GetHostFxrPath(new GetHostFxrParameters { DotNetRoot = path })
        );
    }

    private static string? ReadString(void* pointer) =>
        OperatingSystem.IsWindows()
            ? Marshal.PtrToStringUni((nint)pointer)
            : Marshal.PtrToStringUTF8((nint)pointer);

    private static int WritePath(void* buffer, nuint* length, string path)
    {
        var encoding = OperatingSystem.IsWindows() ? Encoding.Unicode : Encoding.UTF8;
        var bytes = encoding.GetBytes(path + '\0');
        var charSize = OperatingSystem.IsWindows() ? sizeof(char) : sizeof(byte);
        var required = (nuint)(bytes.Length / charSize);
        var capacity = *length;
        *length = required;
        if (capacity < required)
            return ErrorCodes.HostApiBufferTooSmall;
        bytes.CopyTo(new Span<byte>(buffer, checked((int)capacity * charSize)));
        return ErrorCodes.Success;
    }
}
