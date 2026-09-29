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

    [TestCase(ErrorCodes.InvalidArgFailure, NetHostError.InvalidArgFailure)]
    [TestCase(ErrorCodes.CoreHostLibMissingFailure, NetHostError.HostFxrNotFound)]
    [TestCase(ErrorCodes.CoreHostLibLoadFailure, NetHostError.HostFxrLoadFailure)]
    [TestCase(ErrorCodes.CoreHostEntryPointFailure, NetHostError.HostFxrEntryPointNotFound)]
    [TestCase(ErrorCodes.CoreHostCurHostFindFailure, NetHostError.CurrentHostNotFound)]
    [TestCase(-1, NetHostError.Unrecoverable)]
    public void NativeFailuresAreReturnedWithoutDecodingTheBuffer(int code, NetHostError expected)
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
        Assert.That(error, Is.EqualTo(expected));
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
        Assert.That(result.Error, Is.EqualTo(NetHostError.HostFxrNotFound));
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
        Assert.That(result.Error, Is.EqualTo(NetHostError.BufferTooSmall));
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
        Assert.That(result.Error, Is.EqualTo(NetHostError.Unrecoverable));
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
        Assert.That(result.Error, Is.EqualTo(NetHostError.Unrecoverable));
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
        Assert.That(result.Error, Is.EqualTo(NetHostError.Unrecoverable));
    }

    [TestCase(NetHostError.NativeLibraryNotFound)]
    [TestCase(NetHostError.NativeEntryPointNotFound)]
    [TestCase(NetHostError.NativeLibraryIncompatible)]
    public void NativeLoadingFailuresHaveDistinctErrors(NetHostError expected)
    {
        var result = NativeMethods.ResolvePath(
            null,
            (buffer, length, parameters) =>
                throw (
                    expected switch
                    {
                        NetHostError.NativeLibraryNotFound => new DllNotFoundException(),
                        NetHostError.NativeEntryPointNotFound => new EntryPointNotFoundException(),
                        _ => (Exception)new BadImageFormatException(),
                    }
                )
        );
        Assert.That(result.Error, Is.EqualTo(expected));
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
