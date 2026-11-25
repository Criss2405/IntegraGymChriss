using MySqlConnector;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace IntegraGym;

internal sealed record BackupResult(string FilePath, string? AuditWarning);

internal static class BackupService
{
    public static async Task<BackupResult> CreateAsync(string destination, string? account = null,
        string? password = null, string? dumpExecutable = null, CancellationToken cancellationToken = default)
    {
        AppSession.Require("respaldos.crear");
        return await CreateCoreAsync(DatabaseSettings.ConnectionString, destination, account, password, dumpExecutable, cancellationToken);
    }
    internal static async Task<BackupResult> CreateCoreAsync(string connectionString, string destination, string? account = null, string? password = null, string? dumpExecutable = null, CancellationToken cancellationToken = default)
    {
        var settings = new MySqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(settings.Database)) throw new InvalidOperationException("Configura primero la conexión de la aplicación.");
        if (!string.IsNullOrWhiteSpace(account)) { settings.UserID = account.Trim(); settings.Password = password ?? ""; }
        settings.ConnectionTimeout = 10;
        using var connection = new MySqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await RequireCompleteBackupPrivileges(connection, settings.Database, cancellationToken);
        using (var engines = new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_TYPE='BASE TABLE' AND ENGINE<>'InnoDB'", connection))
        {
            if (Convert.ToInt32(await engines.ExecuteScalarAsync(cancellationToken)) > 0)
                throw new InvalidOperationException("Hay tablas que no usan InnoDB. Este respaldo requiere tablas InnoDB para mantener una copia consistente.");
        }
        string executable = dumpExecutable ?? FindDumpExecutable(connection.ServerVersion)
            ?? throw new FileNotFoundException("No se encontró mysqldump.exe. Instala MySQL Server o selecciona esa herramienta en Opciones de respaldo.");
        if (!File.Exists(executable)) throw new FileNotFoundException("La herramienta de respaldo seleccionada ya no existe.");

        string finalPath = Path.GetFullPath(destination);
        if (Directory.Exists(finalPath)) throw new IOException("Selecciona un nombre de archivo SQL, no una carpeta.");
        string directory = Path.GetDirectoryName(finalPath)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("La carpeta de destino ya no está disponible.");
        string partialPath = Path.Combine(directory, "." + Path.GetFileName(finalPath) + "." + Guid.NewGuid().ToString("N") + ".partial");
        string privateDirectory = Path.Combine(Path.GetTempPath(), "IntegraGym-backup-" + Guid.NewGuid().ToString("N"));
        string optionFile = Path.Combine(privateDirectory, "client.cnf");
        try
        {
            var info = Directory.CreateDirectory(privateDirectory);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            info.SetAccessControl(security);
            string ssl = settings.SslMode switch
            {
                MySqlSslMode.None => "DISABLED", MySqlSslMode.Preferred => "PREFERRED",
                MySqlSslMode.Required => "REQUIRED", MySqlSslMode.VerifyCA => "VERIFY_CA", _ => "VERIFY_IDENTITY"
            };
            string options = "[client]\n" + "host=" + Option(settings.Server) + "\nport=" + settings.Port +
                "\nuser=" + Option(settings.UserID) + "\npassword=" + Option(settings.Password) +
                "\nprotocol=TCP\nssl-mode=" + ssl + "\n";
            await File.WriteAllTextAsync(optionFile, options, new UTF8Encoding(false), cancellationToken);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardError = true, RedirectStandardOutput = true
            };
            foreach (string arg in new[] { "--defaults-file=" + optionFile, "--no-login-paths", "--single-transaction",
                "--skip-lock-tables", "--no-tablespaces", "--set-gtid-purged=OFF", "--column-statistics=0",
                "--default-character-set=utf8mb4", "--hex-blob", "--routines", "--events", "--triggers", "--comments",
                "--result-file=" + partialPath, "--databases", settings.Database }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("No se pudo iniciar la herramienta de respaldo.");
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            try { await process.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(errorTask, outputTask);
                throw;
            }
            string error = await errorTask; await outputTask;
            if (process.ExitCode != 0)
            {
                if (!string.IsNullOrEmpty(settings.Password)) error = error.Replace(settings.Password, "[oculta]", StringComparison.Ordinal);
                throw new InvalidOperationException("MySQL no completó el respaldo. Se conservó cualquier archivo anterior.\n\n" + error.Trim());
            }
            if (!File.Exists(partialPath) || new FileInfo(partialPath).Length == 0)
                throw new IOException("MySQL terminó sin generar un archivo de respaldo válido.");
            // Publish only a finished dump, leaving an older destination intact on failure or cancellation.
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partialPath, finalPath, overwrite: true);
            await RestoreVerifier.WriteManifestAsync(finalPath, settings.Database, connection.ServerVersion);
            string? warning = null;
            try { using var auditConnection = new MySqlConnection(connectionString); await auditConnection.OpenAsync(cancellationToken); AuditLog.Write(auditConnection, null, "Crear copia de seguridad", "Base de datos"); }
            catch (MySqlException) { warning = "La copia se guardó, pero no fue posible registrar esta acción en la bitácora."; }
            return new BackupResult(finalPath, warning);
        }
        finally
        {
            DeleteFile(partialPath);
            DeleteFile(optionFile);
            try { if (Directory.Exists(privateDirectory)) Directory.Delete(privateDirectory, false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task RequireCompleteBackupPrivileges(MySqlConnection connection, string database, CancellationToken token)
    {
        var global = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var local = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = new MySqlCommand("SHOW GRANTS", connection);
        using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            string grant = reader.GetString(0);
            int on = grant.IndexOf(" ON ", StringComparison.OrdinalIgnoreCase);
            int to = grant.IndexOf(" TO ", StringComparison.OrdinalIgnoreCase);
            if (!grant.StartsWith("GRANT ", StringComparison.OrdinalIgnoreCase) || on < 0 || to <= on) continue;
            string scope = grant[(on + 4)..to].Replace("`", "");
            if (scope != "*.*" && !scope.Equals(database + ".*", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (string privilege in grant[6..on].Split(','))
            {
                local.Add(privilege.Trim());
                if (scope == "*.*") global.Add(privilege.Trim());
            }
        }
        bool tables = local.Contains("ALL PRIVILEGES") || new[] { "SELECT", "SHOW VIEW", "TRIGGER", "EVENT" }.All(local.Contains);
        bool routines = global.Contains("ALL PRIVILEGES") || global.Contains("SHOW_ROUTINE") || global.Contains("SELECT");
        if (!tables || !routines)
            throw new InvalidOperationException("La cuenta MySQL actual no tiene todos los permisos para una copia completa. Activa «Usar otra cuenta de MySQL» e introduce root u otra cuenta autorizada. Su contraseña se utilizará solo para este respaldo y no se guardará.\n\nSe requieren SELECT, SHOW VIEW, TRIGGER y EVENT sobre la base y acceso a las definiciones de rutinas (SHOW_ROUTINE).");
    }

    public static string? FindDumpExecutable(string serverVersion)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MySQL");
        if (!Directory.Exists(root)) return null;
        string[] parts = serverVersion.Split('.');
        string preferred = parts.Length >= 2 ? "MySQL Server " + parts[0] + "." + parts[1] : "";
        return Directory.GetDirectories(root).OrderByDescending(path => Path.GetFileName(path).Equals(preferred, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .SelectMany(path => new[] { Path.Combine(path, "bin", "mysqldump.exe"), Path.Combine(path, "mysqldump.exe") })
            .FirstOrDefault(File.Exists);
    }

    private static string Option(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
    private static void DeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}


