using System.Runtime.InteropServices;
using Polyester;
using Universe.Hosting.HostFxr.Errors;

namespace Universe.Hosting.HostFxr;

/// <summary>A runtime context with managed properties, assembly loading, and delegate binding.</summary>
public sealed class HostFxrHandle : IDisposable
{
    private readonly HostContextHandle _context;
    private readonly Lock _sync = new();
    private bool _disposed;

    internal HostFxrHandle(HostContextHandle context, InitializationStatus initializationStatus)
    {
        _context = context;
        InitializationStatus = initializationStatus;
    }

    /// <summary>Reports successful initialization, including differences from an already loaded runtime.</summary>
    public InitializationStatus InitializationStatus { get; }

    public Result<string, HostFxrError> GetRuntimePropertyValue(string name)
    {
        NativeString.Validate(name, nameof(name));
        return Invoke(() =>
        {
            var code = NativeMethods.GetRuntimeProperty(_context, name, out var value);
            return code == 0 && value is not null
                ? Result<string, HostFxrError>.Success(value)
                : Result<string, HostFxrError>.Failure(
                    code == 0
                        ? new PropertyNotFound(name)
                        : HostFxrError.MapErrorCode(
                            code,
                            new NativeErrorContext("hostfxr_get_runtime_property_value", PropertyName: name)
                        )
                );
        });
    }

    /// <summary>Sets a runtime property before the runtime is loaded. A null value removes it.</summary>
    public Result<Unit, HostFxrError> SetRuntimePropertyValue(string name, string? value)
    {
        NativeString.Validate(name, nameof(name));
        if (value is not null)
            NativeString.Validate(value, nameof(value), allowEmpty: true);
        return Invoke(() => FromCode(
            NativeMethods.SetRuntimeProperty(_context, name, value),
            new NativeErrorContext("hostfxr_set_runtime_property_value", PropertyName: name)
        ));
    }

    /// <summary>Returns a managed snapshot of the context's runtime properties.</summary>
    public Result<IReadOnlyDictionary<string, string>, HostFxrError> GetRuntimeProperties() =>
        Invoke(() =>
        {
            var code = NativeMethods.GetRuntimeProperties(_context, out var properties);
            return code == 0
                ? Result<IReadOnlyDictionary<string, string>, HostFxrError>.Success(
                    properties
                )
                : Result<IReadOnlyDictionary<string, string>, HostFxrError>.Failure(
                    HostFxrError.MapErrorCode(code, new NativeErrorContext("hostfxr_get_runtime_properties"))
                );
        });

    /// <summary>Loads an assembly and binds a public static method to a concrete delegate type.</summary>
    /// <param name="assemblyPath">Path to the component assembly.</param>
    /// <param name="typeName">Assembly-qualified name of the declaring type in the hosted runtime.</param>
    /// <param name="methodName">Name of the public static method.</param>
    /// <param name="delegateTypeName">Assembly-qualified delegate type name in the hosted runtime.
    /// Its unmanaged signature must match TDelegate. Use a non-generic delegate type.</param>
    /// <remarks>Delegates cross a native ABI boundary: their parameters must be marshalable.
    /// Arbitrary managed objects cannot be shared between the host and the hosted runtime.</remarks>
    public Result<TDelegate, HostFxrError> LoadAssemblyAndGetDelegate<TDelegate>(
        string assemblyPath,
        string typeName,
        string methodName,
        string delegateTypeName
    )
        where TDelegate : Delegate
    {
        NativeString.Validate(assemblyPath, nameof(assemblyPath));
        ValidateDelegate<TDelegate>(delegateTypeName);
        return Bind(Path.GetFullPath(assemblyPath), typeName, methodName, delegateTypeName)
            .Convert(pointer => Marshal.GetDelegateForFunctionPointer<TDelegate>(pointer));
    }

    /// <summary>Binds a method in the default load context to a concrete, ABI-compatible delegate.</summary>
    public Result<TDelegate, HostFxrError> GetDelegate<TDelegate>(
        string typeName,
        string methodName,
        string delegateTypeName
    )
        where TDelegate : Delegate
    {
        ValidateDelegate<TDelegate>(delegateTypeName);
        return Bind(null, typeName, methodName, delegateTypeName)
            .Convert(pointer => Marshal.GetDelegateForFunctionPointer<TDelegate>(pointer));
    }

    /// <summary>Loads an assembly and wraps the default component entry point as a C# delegate.</summary>
    /// <remarks>The hosted method must have signature int(nint data, int size).
    /// The delegate passes a copy of TData using its in-memory size, and returns the method's exit code.</remarks>
    public Result<ComponentEntryPoint<TData>, HostFxrError> LoadAssemblyAndGetEntryPoint<TData>(
        string assemblyPath, string typeName, string methodName
    )
        where TData : unmanaged
    {
        NativeString.Validate(assemblyPath, nameof(assemblyPath));
        return Bind(Path.GetFullPath(assemblyPath), typeName, methodName, null)
            .Convert(CreateEntryPoint<TData>);
    }

    /// <summary>Wraps a default component entry point from the default load context.</summary>
    public Result<ComponentEntryPoint<TData>, HostFxrError> GetEntryPoint<TData>(
        string typeName,
        string methodName
    )
        where TData : unmanaged =>
        Bind(null, typeName, methodName, null).Convert(CreateEntryPoint<TData>);

    public Result<Unit, HostFxrError> LoadAssembly(string assemblyPath)
    {
        NativeString.Validate(assemblyPath, nameof(assemblyPath));
        var fullPath = Path.GetFullPath(assemblyPath);
        return Invoke(() =>
            GetRuntimeDelegate(HostFxrDelegateType.LoadAssembly)
                .Convert(pointer => FromCode(
                    CoreClrDelegates.LoadAssembly(pointer, fullPath),
                    new NativeErrorContext("load_assembly", fullPath)
                ))
        );
    }

    /// <summary>Loads assembly and optional symbol bytes into the default load context.</summary>
    public Result<Unit, HostFxrError> LoadAssemblyBytes(
        ReadOnlySpan<byte> assemblyBytes,
        ReadOnlySpan<byte> symbolsBytes = default
    )
    {
        if (assemblyBytes.IsEmpty)
            throw new ArgumentException("Assembly bytes cannot be empty.", nameof(assemblyBytes));
        // Spans cannot be captured by the callback used in Invoke.
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                var result = GetRuntimeDelegate(HostFxrDelegateType.LoadAssemblyBytes);
                return result.TrySuccess(out var pointer)
                    ? FromCode(
                        CoreClrDelegates.LoadAssemblyBytes(pointer, assemblyBytes, symbolsBytes),
                        new NativeErrorContext("load_assembly_bytes", "assembly bytes")
                    )
                    : Result<Unit, HostFxrError>.Failure(result.Error);
            }
            catch (NativeExportNotFoundException error)
            {
                return Result<Unit, HostFxrError>.Failure(
                    new EntryPointNotFound(error.ExportName)
                );
            }
            finally
            {
                GC.KeepAlive(_context);
            }
        }
    }

    /// <summary>Runs a command-line context synchronously and returns the application's exit code.</summary>
    /// <remarks>hostfxr also returns native failure codes through this value. Negative application exit
    /// codes are preserved. The context must have been initialized for a dotnet command line.</remarks>
    public int RunApp()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                return NativeMethods.RunApp(_context);
            }
            finally
            {
                GC.KeepAlive(_context);
            }
        }
    }

    private Result<nint, HostFxrError> Bind(
        string? assemblyPath,
        string typeName,
        string methodName,
        string? delegateTypeName
    )
    {
        NativeString.Validate(typeName, nameof(typeName));
        NativeString.Validate(methodName, nameof(methodName));
        return Invoke(() =>
        {
            var type = assemblyPath is null
                ? HostFxrDelegateType.GetFunctionPointer
                : HostFxrDelegateType.LoadAssemblyAndGetFunctionPointer;
            return GetRuntimeDelegate(type)
                .Convert(function =>
                {
                    var code = CoreClrDelegates.GetFunctionPointer(
                        function,
                        assemblyPath,
                        typeName,
                        methodName,
                        delegateTypeName,
                        out var pointer
                    );
                    return PointerResult(
                        code, pointer,
                        new NativeErrorContext(
                            assemblyPath is null ? "get_function_pointer" : "load_assembly_and_get_function_pointer",
                            assemblyPath ?? "", TypeName: typeName, MethodName: methodName,
                            DelegateTypeName: delegateTypeName ?? "component entry point"
                        )
                    );
                });
        });
    }

    private Result<nint, HostFxrError> GetRuntimeDelegate(HostFxrDelegateType type)
    {
        var code = NativeMethods.GetRuntimeDelegate(_context, type, out var pointer);
        return PointerResult(
            code, pointer,
            new NativeErrorContext($"hostfxr_get_runtime_delegate ({type})")
        );
    }

    private ComponentEntryPoint<TData> CreateEntryPoint<TData>(nint pointer)
        where TData : unmanaged =>
        data =>
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                try
                {
                    return CoreClrDelegates.InvokeEntryPoint(pointer, data);
                }
                finally
                {
                    GC.KeepAlive(_context);
                }
            }
        };

    private Result<T, HostFxrError> Invoke<T>(Func<Result<T, HostFxrError>> operation)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                return operation();
            }
            catch (NativeExportNotFoundException error)
            {
                return Result<T, HostFxrError>.Failure(new EntryPointNotFound(error.ExportName));
            }
            finally
            {
                GC.KeepAlive(_context);
            }
        }
    }

    private static void ValidateDelegate<TDelegate>(string delegateTypeName)
        where TDelegate : Delegate
    {
        NativeString.Validate(delegateTypeName, nameof(delegateTypeName));
        if (
            typeof(TDelegate).IsGenericType
            || typeof(TDelegate) == typeof(Delegate)
            || typeof(TDelegate) == typeof(MulticastDelegate)
        )
            throw new ArgumentException(
                "Use a concrete, non-generic delegate type.",
                nameof(TDelegate)
            );
    }

    private static Result<nint, HostFxrError> PointerResult(
        int code, nint pointer, NativeErrorContext context
    ) =>
        code == 0 && pointer != 0
            ? Result<nint, HostFxrError>.Success(pointer)
            : Result<nint, HostFxrError>.Failure(
                code == 0
                    ? new HostInvalidState(context.Operation)
                    : HostFxrError.MapErrorCode(code, context)
            );

    private static Result<Unit, HostFxrError> FromCode(int code, NativeErrorContext context) =>
        code == 0
            ? Result<Unit, HostFxrError>.Success(Unit.Instance)
            : Result<Unit, HostFxrError>.Failure(HostFxrError.MapErrorCode(code, context));

    public void Close() => Dispose();

    /// <summary>Closes this context once. Closing a context does not unload the process runtime.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _context.Dispose();
        }
    }
}

/// <summary>A managed wrapper around a component entry point, returning its exit code.</summary>
public delegate int ComponentEntryPoint<in T>(T data)
    where T : unmanaged;
