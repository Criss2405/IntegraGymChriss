using MySqlConnector;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace IntegraGym;

internal sealed record BackupSchedule(string Connection,string Directory,int Hour,bool Enabled);
internal sealed record BackupHistory(DateTimeOffset Time,bool Success,string Message);
internal static class ScheduledBackup
{
    private static string TaskName=>"IntegraGym-Backup-"+WindowsIdentity.GetCurrent().User!.Value;
    public static BackupSchedule? Load()=>LocalSecrets.Read("schedule.bin") is string value?JsonSerializer.Deserialize<BackupSchedule>(value):null;
    public static string HistoryPath=>Path.Combine(LocalSecrets.Folder,"last-backup.json");
    public static BackupHistory? History()=>File.Exists(HistoryPath)?JsonSerializer.Deserialize<BackupHistory>(File.ReadAllText(HistoryPath)):null;
    public static async Task ConfigureAsync(string directory,int hour,string rootAccount,string rootPassword)
    {
        SecurityService.RequireAdmin();if(hour<0||hour>23)throw new ArgumentOutOfRangeException(nameof(hour));if(!Directory.Exists(directory))throw new DirectoryNotFoundException("Selecciona una carpeta existente.");
        Register(hour,true); // Validate Windows task access before creating a database account.
        var b=new MySqlConnectionStringBuilder(DatabaseSettings.ConnectionString);string database=b.Database;
        if(!System.Text.RegularExpressions.Regex.IsMatch(database,"^[A-Za-z0-9_]+$"))throw new InvalidOperationException("Nombre de base no compatible con el asistente de respaldos.");
        string backupUser="ig_backup_"+Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        b.UserID=rootAccount;b.Password=rootPassword;
        using var c=new MySqlConnection(b.ConnectionString);await c.OpenAsync();
        using(var q=new MySqlCommand($"CREATE USER '{backupUser}'@'localhost' IDENTIFIED BY @secret",c)){q.Parameters.AddWithValue("@secret",secret);await q.ExecuteNonQueryAsync();}
        try
        {
            using(var q=new MySqlCommand($"GRANT SELECT,SHOW VIEW,TRIGGER,EVENT ON `{database}`.* TO '{backupUser}'@'localhost'; GRANT SHOW_ROUTINE ON *.* TO '{backupUser}'@'localhost';",c))await q.ExecuteNonQueryAsync();
            b.UserID=backupUser;b.Password=secret;
            var schedule=new BackupSchedule(b.ConnectionString,directory,hour,true);
            // Test destination and privileges before enabling a scheduled task.
            await BackupService.CreateCoreAsync(b.ConnectionString,Path.Combine(directory,$"IntegraGym-prueba-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.sql"));
            string? previous=LocalSecrets.Read("schedule.bin");LocalSecrets.Save("schedule.bin",JsonSerializer.Serialize(schedule));
            try { Register(hour); }
            catch { if(previous is null)File.Delete(Path.Combine(LocalSecrets.Folder,"schedule.bin"));else LocalSecrets.Save("schedule.bin",previous);throw; }
            AuditLog.Record("Programar respaldo diario","Configuracion");
        }
        catch
        {
            // A successfully registered task must retain its account even if the audit fails.
            if(Load()?.Connection.Contains(backupUser)!=true){using var q=new MySqlCommand($"DROP USER IF EXISTS '{backupUser}'@'localhost'",c);await q.ExecuteNonQueryAsync();}
            throw;
        }
    }
    public static void Disable()
    {
        SecurityService.RequireAdmin();dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;service.Connect();
        try{service.GetFolder("\\").DeleteTask(TaskName,0);}catch(System.Runtime.InteropServices.COMException ex) when((uint)ex.HResult==0x80070002){}
        var current=Load();if(current is not null)LocalSecrets.Save("schedule.bin",JsonSerializer.Serialize(current with{Enabled=false}));AuditLog.Record("Desactivar respaldo programado","Configuracion");
    }
    private static void Register(int hour,bool validateOnly=false)
    {
        dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;service.Connect();dynamic task=service.NewTask(0);
        task.RegistrationInfo.Description="Respaldo diario local de Integra Gym. Requiere sesión de Windows iniciada y MySQL encendido.";
        task.Principal.LogonType=3;task.Principal.RunLevel=0;task.Settings.StartWhenAvailable=true;task.Settings.DisallowStartIfOnBatteries=false;task.Settings.StopIfGoingOnBatteries=false;task.Settings.MultipleInstances=2;task.Settings.ExecutionTimeLimit="PT1H";
        dynamic daily=task.Triggers.Create(2);daily.StartBoundary=DateTime.Today.AddHours(hour).ToString("yyyy-MM-ddTHH:mm:ss");daily.DaysInterval=1;
        dynamic logon=task.Triggers.Create(9);logon.UserId=WindowsIdentity.GetCurrent().Name;
        dynamic action=task.Actions.Create(0);action.Path=Environment.ProcessPath!;action.Arguments="--scheduled-backup";action.WorkingDirectory=AppContext.BaseDirectory;
        service.GetFolder("\\").RegisterTaskDefinition(TaskName,task,validateOnly?1:6,WindowsIdentity.GetCurrent().Name,null,3);
    }
    internal static async Task<int> RunAsync()
    {
        Directory.CreateDirectory(LocalSecrets.Folder);
        FileStream runLock; try { runLock = new FileStream(Path.Combine(LocalSecrets.Folder,"backup.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); } catch(IOException) { return 0; }
        try
        {
            var s=Load();if(s is null||!s.Enabled)return 0;
            var last=History();DateTime due=DateTime.Today.AddHours(s.Hour);if(DateTime.Now<due)due=due.AddDays(-1);
            if(last is {Success:true}&&last.Time.LocalDateTime>=due)return 0;
            var result=await BackupService.CreateCoreAsync(s.Connection,Path.Combine(s.Directory,$"IntegraGym-auto-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.sql"));
            SaveHistory(true,result.FilePath);try { AuditLog.Record("Completar respaldo programado","Base de datos"); } catch(MySqlException) { } return 0;
        }
        catch(Exception ex){SaveHistory(false,ex is MySqlException?"MySQL no disponible o sin permisos. Revisa la programación.":ex.Message);return 1;}
        finally { runLock.Dispose(); }
    }
    private static void SaveHistory(bool success,string message){Directory.CreateDirectory(LocalSecrets.Folder);string path=HistoryPath+".tmp";File.WriteAllText(path,JsonSerializer.Serialize(new BackupHistory(DateTimeOffset.Now,success,message)));File.Move(path,HistoryPath,true);}
}

internal sealed class BackupCenterForm : StudioForm, IWorkspaceActivity
{
    private readonly Label status=new(){AutoSize=true,MaximumSize=new Size(640,0)};
    private bool busy;
    public bool IsBusy => busy;
    public BackupCenterForm()
    {
        SecurityService.RequireAdmin();Text="Respaldos automáticos y verificación";Size=new Size(780,720);StartPosition=FormStartPosition.CenterParent;var p=SettingsUi.Panel(this);
        SettingsUi.Note(p,"Respaldo diario con la aplicación cerrada. La computadora debe estar encendida, tu sesión de Windows iniciada y MySQL disponible. Al iniciar sesión se recupera una ejecución pendiente. No se borran copias anteriores.");
        var folder=new TextBox();var hour=new NumericUpDown{Minimum=0,Maximum=23,Value=20};var saved=ScheduledBackup.Load();if(saved is not null){folder.Text=saved.Directory;hour.Value=saved.Hour;}
        SettingsUi.Field(p,"Carpeta para los respaldos",folder);SettingsUi.Action(p,"Elegir carpeta…",()=>{using var d=new FolderBrowserDialog();if(d.ShowDialog(this)==DialogResult.OK)folder.Text=d.SelectedPath;});SettingsUi.Field(p,"Hora diaria (0 a 23, hora de Windows)",hour);
        SettingsUi.Action(p,"Activar programación y probar respaldo…",async()=>await Run(async()=>
        {
            using var credentials=new CredentialsForm("Autorizar cuenta de respaldo","Se creará una cuenta MySQL dedicada. Su contraseña se guarda cifrada para este usuario de Windows. La contraseña de root no se conserva.");
            if(credentials.ShowDialog(this)!=DialogResult.OK)return;
            await ScheduledBackup.ConfigureAsync(folder.Text,(int)hour.Value,credentials.Account,credentials.Secret);status.Text="Programación activada y respaldo inicial comprobado.";
        }));
        SettingsUi.Action(p,"Desactivar programación",()=>{ScheduledBackup.Disable();status.Text="Programación desactivada. Se conservaron las copias.";});
        SettingsUi.Action(p,"Ver último resultado",()=>{var h=ScheduledBackup.History();status.Text=h is null?"Todavía no hay ejecuciones automáticas.":$"{h.Time.LocalDateTime:g} · {(h.Success?"Correcto":"Falló")}\n{h.Message}";});
        SettingsUi.Action(p,"Reintentar respaldo pendiente",async()=>await Run(async()=>{int result=await ScheduledBackup.RunAsync();status.Text=result==0?"Revisión terminada. Consulta el último resultado; si ya había copia del período, no se duplicó.":"El respaldo falló. Consulta el último resultado.";}));
        SettingsUi.Action(p,"Verificar restauración de una copia…",async()=>await Run(async()=>
        {
            using var d=new OpenFileDialog{Filter="Respaldo de Integra Gym (*.sql)|*.sql"};if(d.ShowDialog(this)!=DialogResult.OK)return;
            if(MessageBox.Show(this,"Se importará esta copia en un servidor MySQL temporal aislado, sin conectarlo a tu base actual. Usa solo copias propias. Puede tardar varios minutos. ¿Continuar?","Verificar recuperación",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            status.Text="Inicializando una instancia temporal e importando la copia…";status.Text=await RestoreVerifier.VerifyAsync(d.FileName);AuditLog.Record("Verificar restauración aislada","Base de datos");
        }));p.Controls.Add(status);FormClosing+=(_,e)=>{if(busy)e.Cancel=true;};
    }
    private async Task Run(Func<Task> action){if(busy)return;busy=true;Controls[0].Enabled=false;try{await action();}catch(Exception ex){status.Text="No se completó: "+ex.Message;}finally{busy=false;Controls[0].Enabled=true;}}
}




