namespace Universe.Hosting.HostFxr.Extensions;

internal static class DllNotFoundExceptionExtensions
{
    extension(DllNotFoundException exception)
    {
        public static void ThrowIfNotFound(string dllPath)
        {
            if (!File.Exists(dllPath))
            {
                throw new DllNotFoundException($"The native library '{dllPath}' was not found.");
            }
        }
    }
}
