using System.Runtime.CompilerServices;

namespace Universe.Hosting.HostFxr.Exceptions;

public class HostFxrException(string message) : Exception(message)
{
    public static void ThrowIfJit()
    {
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            throw new HostFxrException(
                "This API is only supported in Native AOT applications and cannot be used under JIT."
            );
        }
    }
}