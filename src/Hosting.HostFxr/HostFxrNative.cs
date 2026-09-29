using System.Runtime.InteropServices;

namespace Universe.Hosting.HostFxr;

internal static class HostFxrNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct InitializeParameters
    {
        public nuint Size;
        public nint HostPath;
        public nint DotNetRoot;
    }
}