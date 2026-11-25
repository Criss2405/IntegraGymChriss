using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    private const string AppName = "Integra Gym";
    private const string AppExe = "IntegraGym.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return args.Any(arg => string.Equals(arg, "--uninstall", StringComparison.OrdinalIgnoreCase))
                ? Uninstall()
                : Install();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"No se pudo completar la operación: {ex.Message}");
            WaitForEnter();
            return 1;
        }
    }

    private static int Install()
    {
        var package = Path.Combine(AppContext.BaseDirectory, "package-exe");
        if (!File.Exists(Path.Combine(package, AppExe)))
            throw new FileNotFoundException("No se encontró package-exe\\IntegraGym.exe. Extrae todo el ZIP y vuelve a abrir el instalador.");
        if (!HasNet10DesktopRuntime())
            throw new InvalidOperationException("Se necesita .NET Desktop Runtime 10. Instálalo desde Microsoft y vuelve a ejecutar este instalador.");

        var installDir = GetInstallDirectory();
        Directory.CreateDirectory(installDir);
        CopyDirectory(package, installDir);

        var installerSource = Environment.ProcessPath ?? throw new InvalidOperationException("No se pudo localizar el instalador.");
        var installerInstalled = Path.Combine(installDir, "IntegraGym-Setup.exe");
        if (!Path.GetFullPath(installerSource).Equals(Path.GetFullPath(installerInstalled), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(installerSource, installerInstalled, overwrite: true);
            foreach(var name in new[] { "IntegraGymSetup.dll", "IntegraGymSetup.deps.json", "IntegraGymSetup.runtimeconfig.json" })
                File.Copy(Path.Combine(AppContext.BaseDirectory,name),Path.Combine(installDir,name),overwrite:true);
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
        Directory.CreateDirectory(startMenu);
        var appPath = Path.Combine(installDir, AppExe);
        CreateShortcut(Path.Combine(desktop, "Integra Gym.lnk"), appPath, installDir, "Sistema local de gestión Integra Gym");
        CreateShortcut(Path.Combine(startMenu, "Integra Gym.lnk"), appPath, installDir, "Sistema local de gestión Integra Gym");
        CreateShortcut(Path.Combine(startMenu, "Desinstalar Integra Gym.lnk"), installerInstalled, installDir,
            "Quita la aplicación; conserva la base MySQL local", "--uninstall");

        Console.WriteLine("Integra Gym quedó instalado.");
        Console.WriteLine("Abre el acceso directo del Escritorio o del menú Inicio.");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INTEGRAGYM_DB_CONNECTION", EnvironmentVariableTarget.User)) &&
            !File.Exists(Path.Combine(installDir, "dbconnection.bin")))
            Console.WriteLine("Al abrir la aplicación, usa el asistente Configurar conexión para conectar con MySQL.");
        Console.WriteLine("La base de datos MySQL y su contraseña no se copiaron ni modificaron.");
        WaitForEnter();
        return 0;
    }

    private static int Uninstall()
    {
        Console.WriteLine("Se quitarán los archivos de Integra Gym y sus accesos directos.");
        Console.WriteLine("La base MySQL y los respaldos se conservarán.");
        Console.Write("Escribe SI para continuar: ");
        if (!string.Equals(Console.ReadLine()?.Trim(), "SI", StringComparison.Ordinal))
        {
            Console.WriteLine("Desinstalación cancelada.");
            WaitForEnter();
            return 0;
        }

        var installDir = GetInstallDirectory();
        var desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Integra Gym.lnk");
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
        TryDeleteFile(desktopShortcut);
        TryDeleteDirectory(startMenu);

        var cleanup = Path.Combine(Path.GetTempPath(), $"IntegraGym-Uninstall-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(cleanup,
            "@echo off\r\ntimeout /t 2 /nobreak >nul\r\n" +
            $"rmdir /s /q \"{installDir}\"\r\n" +
            "del \"%~f0\"\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cleanup}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
        Console.WriteLine("Integra Gym se desinstalará. MySQL y los datos se conservarán.");
        return 0;
    }

    private static string GetInstallDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "IntegraGym");

    private static bool HasNet10DesktopRuntime()
    {
        var runtimeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet", "shared", "Microsoft.WindowsDesktop.App");
        return Directory.Exists(runtimeDir) && Directory.GetDirectories(runtimeDir)
            .Select(Path.GetFileName)
            .Any(name => Version.TryParse(name, out var version) && version.Major == 10);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            if (Path.GetExtension(file).Equals(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory,
        string description, string arguments = "")
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows no pudo crear el acceso directo.");
        dynamic shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Windows no pudo crear el acceso directo.");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.Arguments = arguments;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.IconLocation = targetPath + ",0";
        shortcut.Description = description;
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private static void WaitForEnter()
    {
        if (!Console.IsInputRedirected)
        {
            Console.Write("Presiona Enter para cerrar...");
            Console.ReadLine();
        }
    }
}
