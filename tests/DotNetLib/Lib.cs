using System.Runtime.InteropServices;

namespace DotNetLib;

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
public delegate int AddDelegate(int left, int right);

public static class Lib
{
    public static int Add(int left, int right) => left + right;

    public static int EntryPoint(nint data, int size) =>
        size == sizeof(int) ? Marshal.ReadInt32(data) + 1 : -1;

    public static int Main(string[] args) => args is ["参数 café 😀", "", "two words"] ? 37 : 99;
}
