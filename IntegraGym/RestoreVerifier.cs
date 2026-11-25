using MySqlConnector;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace IntegraGym;

internal sealed record BackupManifest(string Database,string ServerVersion,string Sha256);
internal static class RestoreVerifier
{
    public static async Task WriteManifestAsync(string path,string database,string version)
    {
        await using var stream=File.OpenRead(path);string hash=Convert.ToHexString(await SHA256.HashDataAsync(stream));
        await File.WriteAllTextAsync(path+".json",JsonSerializer.Serialize(new BackupManifest(database,version,hash)));
    }
    public static async Task<string> VerifyAsync(string path)
    {
        SecurityService.RequireAdmin();
        if(!File.Exists(path+".json"))throw new InvalidOperationException("Falta el archivo .sql.json que acompaña a las copias v1.2. Copia ambos archivos o genera una copia nueva.");
        var manifest=JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(path+".json"))??throw new InvalidDataException("Manifiesto inválido.");
        if(!System.Text.RegularExpressions.Regex.IsMatch(manifest.Database,"^[A-Za-z0-9_]+$"))throw new InvalidDataException("Nombre de base inválido.");
        await using var snapshot=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        string hash=Convert.ToHexString(await SHA256.HashDataAsync(snapshot));snapshot.Position=0;
        if(!hash.Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("La copia está incompleta o cambió desde que se creó. No se importará.");
        string dump=BackupService.FindDumpExecutable(manifest.ServerVersion)??throw new FileNotFoundException("Instala MySQL Server para comprobar la restauración.");
        string bin=Path.GetDirectoryName(dump)!, server=Path.Combine(bin,"mysqld.exe"), client=Path.Combine(bin,"mysql.exe");
        if(!File.Exists(server)||!File.Exists(client))throw new FileNotFoundException("Se necesitan mysqld.exe y mysql.exe de MySQL Server.");
        string root=Path.Combine(Path.GetTempPath(),"IntegraGym-restore-"+Guid.NewGuid().ToString("N"));
        var dir=Directory.CreateDirectory(root);var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));dir.SetAccessControl(acl);
        string data=Path.Combine(root,"data"),init=Path.Combine(root,"init.sql"),options=Path.Combine(root,"client.cnf");
        string secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));Process? running=null;
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(10));var token=timeout.Token;
        try
        {
            var initialize=Start(server,"--no-defaults","--initialize-insecure","--basedir="+Path.GetDirectoryName(bin),"--datadir="+data,"--log-error="+Path.Combine(root,"initialize.log"));
            await ExecuteAsync(initialize,token);
            int port;var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            await File.WriteAllTextAsync(init,$"ALTER USER 'root'@'localhost' IDENTIFIED BY '{secret}';",token);
            var start=Start(server,"--no-defaults","--basedir="+Path.GetDirectoryName(bin),"--datadir="+data,"--port="+port,"--bind-address=127.0.0.1","--mysqlx=OFF","--event-scheduler=OFF","--skip-log-bin","--local-infile=OFF","--secure-file-priv="+root,"--init-file="+init,"--log-error="+Path.Combine(root,"server.log"));
            // MySQL logs to a private file; no inherited output pipes to block a long-running server.
            start.RedirectStandardError=false;start.RedirectStandardOutput=false;
            running=Process.Start(start)??throw new IOException("No se pudo iniciar MySQL temporal.");
            var connectionString=new MySqlConnectionStringBuilder{Server="127.0.0.1",Port=(uint)port,UserID="root",Password=secret,SslMode=MySqlSslMode.None,Pooling=false,ConnectionTimeout=1}.ConnectionString;
            bool ready=false;
            for(int i=0;i<90;i++)
            {
                token.ThrowIfCancellationRequested();if(running.HasExited)throw new IOException("MySQL temporal no pudo arrancar. Comprueba que la versión instalada es compatible.");
                try{using var probe=new MySqlConnection(connectionString);await probe.OpenAsync(token);ready=true;break;}catch(MySqlException){await Task.Delay(500,token);}
            }
            if(!ready)throw new TimeoutException("MySQL temporal no respondió.");File.Delete(init);
            await File.WriteAllTextAsync(options,$"[client]\nhost=127.0.0.1\nport={port}\nuser=root\npassword={secret}\nprotocol=TCP\nssl-mode=DISABLED\n",token);
            var restore=Start(client,"--defaults-file="+options,"--no-login-paths","--default-character-set=utf8mb4","--binary-mode=1","--local-infile=0");restore.RedirectStandardInput=true;
            using(var process=Process.Start(restore)!)
            {
                Task<string> errors=process.StandardError.ReadToEndAsync(token),output=process.StandardOutput.ReadToEndAsync(token);
                try{await snapshot.CopyToAsync(process.StandardInput.BaseStream,token);process.StandardInput.Close();await process.WaitForExitAsync(token);await Task.WhenAll(errors,output);if(process.ExitCode!=0)throw new InvalidDataException("La importación falló en la instancia de prueba. Revisa compatibilidad de versión y genera otro respaldo.");}
                finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}}
            }
            using var c=new MySqlConnection(connectionString);await c.OpenAsync(token);
            var report=new StringBuilder($"RESTAURACIÓN AISLADA CORRECTA\nFecha: {DateTime.Now:g}\nBase: {manifest.Database}\nSHA256: {hash}\n");
            foreach(string table in new[]{"Usuarios","Clientes","Planes","Membresias","Pagos","Asistencias","Bitacora","Configuracion","SeguridadUsuarios"})
            {
                using var q=new MySqlCommand($"SELECT COUNT(*) FROM `{manifest.Database}`.`{table}`",c);report.AppendLine($"{table}: {await q.ExecuteScalarAsync(token)} registros");
            }
            using(var q=new MySqlCommand("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=@db",c)){q.Parameters.AddWithValue("@db",manifest.Database);report.AppendLine($"Rutinas: {await q.ExecuteScalarAsync(token)}");}
            report.AppendLine("La base original no se modificó. Los eventos no se ejecutaron. La instancia temporal se elimina al terminar.");
            await File.WriteAllTextAsync(path+".verificacion.txt",report.ToString(),token);return report.ToString();
        }
        finally
        {
            if(running is not null){if(!running.HasExited){running.Kill(true);await running.WaitForExitAsync(CancellationToken.None);}running.Dispose();}
            // This directory was created above with a unique name; never delete any existing server directory.
            string resolved=Path.GetFullPath(root);string parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(resolved.StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(resolved).StartsWith("IntegraGym-restore-"))
            {try{Directory.Delete(resolved,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
    }
    private static ProcessStartInfo Start(string exe,params string[] args){var s=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};foreach(string arg in args)s.ArgumentList.Add(arg);return s;}
    private static async Task ExecuteAsync(ProcessStartInfo start,CancellationToken token)
    {
        using var p=Process.Start(start)??throw new IOException("No se pudo iniciar MySQL.");var error=p.StandardError.ReadToEndAsync(token);var output=p.StandardOutput.ReadToEndAsync(token);
        try{await p.WaitForExitAsync(token);await Task.WhenAll(error,output);if(p.ExitCode!=0)throw new IOException("No se pudo inicializar el servidor de prueba. Comprueba espacio libre y permisos de la carpeta temporal.");}
        finally{if(!p.HasExited){p.Kill(true);await p.WaitForExitAsync(CancellationToken.None);}}
    }
}

