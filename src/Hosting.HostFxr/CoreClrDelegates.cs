namespace Universe.Hosting.HostFxr;

internal static unsafe class CoreClrDelegates
{
    public static int GetFunctionPointer(
        nint function,
        string? assemblyPath,
        string typeName,
        string methodName,
        string? delegateTypeName,
        out nint pointer
    )
    {
        using var assembly = new NativeString(assemblyPath);
        using var type = new NativeString(typeName);
        using var method = new NativeString(methodName);
        using var delegateType = new NativeString(delegateTypeName);
        nint result = 0;
        // CoreCLR delegates use stdcall on Windows; that convention maps to the platform ABI elsewhere.
        var code = assemblyPath is not null
            ? ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, nint, nint*, int>)function)(
                assembly.Pointer,
                type.Pointer,
                method.Pointer,
                delegateType.Pointer,
                0,
                &result
            )
            : ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, nint, nint*, int>)function)(
                type.Pointer,
                method.Pointer,
                delegateType.Pointer,
                0,
                0,
                &result
            );
        pointer = result;
        return code;
    }

    public static int InvokeEntryPoint<TData>(nint pointer, TData data)
        where TData : unmanaged =>
        ((delegate* unmanaged[Stdcall]<void*, int, int>)pointer)(&data, sizeof(TData));

    public static int LoadAssembly(nint pointer, string assemblyPath)
    {
        using var path = new NativeString(assemblyPath);
        return ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)pointer)(path.Pointer, 0, 0);
    }

    public static int LoadAssemblyBytes(
        nint pointer,
        ReadOnlySpan<byte> assemblyBytes,
        ReadOnlySpan<byte> symbolsBytes
    )
    {
        fixed (byte* assembly = assemblyBytes)
        fixed (byte* symbols = symbolsBytes)
            return (
                (delegate* unmanaged[Stdcall]<byte*, nuint, byte*, nuint, nint, nint, int>)pointer
            )(assembly, (nuint)assemblyBytes.Length, symbols, (nuint)symbolsBytes.Length, 0, 0);
    }
}
