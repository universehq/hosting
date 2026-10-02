using Polyester;
using Universe.Hosting.HostFxr.Errors;
using Universe.Hosting.HostFxr.Exceptions;

namespace Universe.Hosting.HostFxr;

/// <summary>Loads hostfxr and creates independently disposable runtime contexts.</summary>
/// <remarks>Runtime hosting is supported in Native AOT applications.</remarks>
public sealed class HostFxr : IDisposable
{
    private readonly NativeLibraryHandle _library;
    private readonly Lock _sync = new();
    private bool _disposed;

    public HostFxr(string hostFxrPath)
    {
        NativeString.Validate(hostFxrPath, nameof(hostFxrPath));
        HostFxrException.ThrowIfJit();
        _library = new NativeLibraryHandle(hostFxrPath);
    }

    /// <summary>Prepares an application using arguments such as ["app.dll", "argument"].</summary>
    public Result<HostFxrHandle, InitializeError> InitializeForDotNetCommandLine(
        string[] args,
        InitializeParameters? parameters = null
    )
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0)
            throw new ArgumentException("An application path is required.", nameof(args));
        foreach (var argument in args)
            NativeString.Validate(argument, nameof(args), allowEmpty: true);
        ValidateParameters(parameters);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                // Resolve close before creating a context so every successful context can be released.
                var close = _library.GetExport("hostfxr_close");
                var code = NativeMethods.InitializeForDotNetCommandLine(
                    _library,
                    args,
                    parameters,
                    out var context
                );
                return CreateContext(code, context, close);
            }
            catch (EntryPointNotFoundException)
            {
                return Result<HostFxrHandle, InitializeError>.Failure(
                    InitializeError.EntryPointNotFound
                );
            }
            finally
            {
                GC.KeepAlive(_library);
            }
        }
    }

    /// <summary>Prepares a component from its .runtimeconfig.json file.</summary>
    public Result<HostFxrHandle, InitializeError> InitializeForRuntimeConfig(
        string runtimeConfigPath,
        InitializeParameters? parameters = null
    )
    {
        NativeString.Validate(runtimeConfigPath, nameof(runtimeConfigPath));
        ValidateParameters(parameters);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                var close = _library.GetExport("hostfxr_close");
                var code = NativeMethods.InitializeForRuntimeConfig(
                    _library,
                    runtimeConfigPath,
                    parameters,
                    out var context
                );
                return CreateContext(code, context, close);
            }
            catch (EntryPointNotFoundException)
            {
                return Result<HostFxrHandle, InitializeError>.Failure(
                    InitializeError.EntryPointNotFound
                );
            }
            finally
            {
                GC.KeepAlive(_library);
            }
        }
    }

    private Result<HostFxrHandle, InitializeError> CreateContext(int code, nint context, nint close)
    {
        var error = InitializeError.MapErrorCode(code);
        if (error != InitializeError.None)
            return Result<HostFxrHandle, InitializeError>.Failure(error);
        if (context == 0)
            return Result<HostFxrHandle, InitializeError>.Failure(InitializeError.HostInvalidState);

        return Result<HostFxrHandle, InitializeError>.Success(
            new HostFxrHandle(
                new HostContextHandle(context, close, _library),
                (InitializationStatus)code
            )
        );
    }

    private static void ValidateParameters(InitializeParameters? parameters)
    {
        if (parameters?.HostPath is { } hostPath)
            NativeString.Validate(hostPath, nameof(InitializeParameters.HostPath));
        if (parameters?.DotNetRoot is { } dotNetRoot)
            NativeString.Validate(dotNetRoot, nameof(InitializeParameters.DotNetRoot));
    }

    /// <summary>Releases this loader. Contexts already created keep the library alive until disposed.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _library.Dispose();
        }
    }
}
