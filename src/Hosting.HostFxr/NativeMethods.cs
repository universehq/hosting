using Universe.Hosting.Common;

using System.Collections.ObjectModel;

using Universe.Hosting.HostFxr.Errors;

namespace Universe.Hosting.HostFxr;

internal static unsafe class NativeMethods
{
    public static int InitializeForDotNetCommandLine(
        NativeLibraryHandle library,
        string[] args,
        InitializeParameters? parameters,
        out nint contextHandle)
    {
        var invoke = (delegate* unmanaged[Cdecl]<int, nint*, HostFxrNative.InitializeParameters*, nint*, int>)
            library.GetExport("hostfxr_initialize_for_dotnet_command_line");
        using var hostPath = new NativeString(parameters?.HostPath);
        using var dotNetRoot = new NativeString(parameters?.DotNetRoot);
        var nativeParameters = CreateParameters(hostPath, dotNetRoot);
        var strings = new NativeString?[args.Length];
        var pointers = new nint[args.Length];
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                strings[i] = new NativeString(args[i]);
                pointers[i] = strings[i]!.Pointer;
            }

            nint context = 0;
            fixed (nint* argv = pointers)
            {
                var code = invoke(args.Length, argv, parameters.HasValue ? &nativeParameters : null, &context);
                contextHandle = context;
                return code;
            }
        }
        finally
        {
            foreach (var value in strings)
                value?.Dispose();
        }
    }

    public static int InitializeForRuntimeConfig(
        NativeLibraryHandle library,
        string runtimeConfigPath,
        InitializeParameters? parameters,
        out nint contextHandle)
    {
        var invoke = (delegate* unmanaged[Cdecl]<nint, HostFxrNative.InitializeParameters*, nint*, int>)
            library.GetExport("hostfxr_initialize_for_runtime_config");
        using var path = new NativeString(runtimeConfigPath);
        using var hostPath = new NativeString(parameters?.HostPath);
        using var dotNetRoot = new NativeString(parameters?.DotNetRoot);
        var nativeParameters = CreateParameters(hostPath, dotNetRoot);
        nint context = 0;
        var code = invoke(path.Pointer, parameters.HasValue ? &nativeParameters : null, &context);
        contextHandle = context;
        return code;
    }

    private static HostFxrNative.InitializeParameters CreateParameters(NativeString hostPath, NativeString dotNetRoot) => new()
    {
        Size = (nuint)sizeof(HostFxrNative.InitializeParameters),
        HostPath = hostPath.Pointer,
        DotNetRoot = dotNetRoot.Pointer,
    };

    public static int GetRuntimeProperty(HostContextHandle context, string name, out string? value)
    {
        var invoke = (delegate* unmanaged[Cdecl]<nint, nint, nint*, int>)
            context.Library.GetExport("hostfxr_get_runtime_property_value");
        using var key = new NativeString(name);
        nint pointer = 0;
        var code = invoke(context.DangerousGetHandle(), key.Pointer, &pointer);
        value = code == 0 ? NativeString.Read(pointer) : null;
        return code;
    }

    public static int SetRuntimeProperty(HostContextHandle context, string name, string? value)
    {
        var invoke = (delegate* unmanaged[Cdecl]<nint, nint, nint, int>)
            context.Library.GetExport("hostfxr_set_runtime_property_value");
        using var key = new NativeString(name);
        using var text = new NativeString(value);
        return invoke(context.DangerousGetHandle(), key.Pointer, text.Pointer);
    }

    public static int GetRuntimeProperties(HostContextHandle context, out IReadOnlyDictionary<string, string> properties)
    {
        var invoke = (delegate* unmanaged[Cdecl]<nint, nuint*, nint*, nint*, int>)
            context.Library.GetExport("hostfxr_get_runtime_properties");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        properties = new ReadOnlyDictionary<string, string>(values);
        nuint count = 0;
        var code = invoke(context.DangerousGetHandle(), &count, null, null);
        if (code != 0 && code != ErrorCodes.HostApiBufferTooSmall)
            return code;
        if (count == 0)
            return 0;

        // Another host can change properties between the size query and the copy.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var capacity = checked((int)count);
            var keys = new nint[capacity];
            var entries = new nint[capacity];
            fixed (nint* keyPointers = keys)
            fixed (nint* valuePointers = entries)
                code = invoke(context.DangerousGetHandle(), &count, keyPointers, valuePointers);
            if (code == ErrorCodes.HostApiBufferTooSmall)
                continue;
            if (code != 0)
                return code;
            for (var i = 0; i < checked((int)count); i++)
                values.Add(NativeString.Read(keys[i])!, NativeString.Read(entries[i])!);
            return 0;
        }
        return code;
    }

    public static int GetRuntimeDelegate(HostContextHandle context, HostFxrDelegateType type, out nint pointer)
    {
        var invoke = (delegate* unmanaged[Cdecl]<nint, HostFxrDelegateType, nint*, int>)
            context.Library.GetExport("hostfxr_get_runtime_delegate");
        nint result = 0;
        var code = invoke(context.DangerousGetHandle(), type, &result);
        pointer = result;
        return code;
    }

    public static int RunApp(HostContextHandle context)
    {
        var invoke = (delegate* unmanaged[Cdecl]<nint, int>)context.Library.GetExport("hostfxr_run_app");
        return invoke(context.DangerousGetHandle());
    }
}