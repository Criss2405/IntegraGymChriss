using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace IntegraGym;

internal static class LocalSecrets
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntegraGym", "Configuration");
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Size=bytes.Length, Data=Marshal.AllocHGlobal(bytes.Length) };
        try
        {
            Marshal.Copy(bytes,0,input.Data,bytes.Length);
            Blob output;
            bool ok = protect ? CryptProtectData(ref input,"Integra Gym",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output)
                : CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if (!ok) throw new CryptographicException("Windows no pudo proteger o leer la configuración de este usuario.");
            try { var result=new byte[output.Size]; Marshal.Copy(output.Data,result,0,result.Length); return result; }
            finally { LocalFree(output.Data); }
        }
        finally { for(int i=0;i<input.Size;i++) Marshal.WriteByte(input.Data,i,0); Marshal.FreeHGlobal(input.Data); }
    }
    public static string? Read(string name)
    {
        string path=Path.Combine(Folder,name);
        return File.Exists(path) ? Encoding.UTF8.GetString(Transform(File.ReadAllBytes(path),false)) : null;
    }
    public static void Save(string name,string value)
    {
        Directory.CreateDirectory(Folder);
        string path=Path.Combine(Folder,name), temporary=path+".tmp";
        byte[] plain=Encoding.UTF8.GetBytes(value);
        try { File.WriteAllBytes(temporary,Transform(plain,true)); File.Move(temporary,path,true); }
        finally { CryptographicOperations.ZeroMemory(plain); if(File.Exists(temporary)) File.Delete(temporary); }
    }
}

