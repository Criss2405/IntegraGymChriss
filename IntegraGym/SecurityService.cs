using MySqlConnector;
using System.Security.Cryptography;

namespace IntegraGym;

internal static class SecurityService
{
    public static void RequireAdmin()
    {
        var u=AppSession.CurrentUser;
        if(u is null)throw new UnauthorizedAccessException("Inicia sesión como administrador.");
        using var c=Open(); using var q=new MySqlCommand("SELECT COUNT(*) FROM Usuarios u JOIN Roles r ON r.RolId=u.RolId WHERE u.UsuarioId=@id AND u.Activo=TRUE AND r.Activo=TRUE AND r.Nombre='Administrador'",c);
        q.Parameters.AddWithValue("@id",u.Id);if(Convert.ToInt32(q.ExecuteScalar())!=1)throw new UnauthorizedAccessException("Esta función requiere un administrador activo.");
    }
    public static MySqlConnection Open(){var c=new MySqlConnection(DatabaseSettings.ConnectionString);c.Open();return c;}
    public static AppUser? Authenticate(string email,string password)
    {
        using var c=Open();using var tx=c.BeginTransaction();
        using var q=new MySqlCommand("SELECT u.UsuarioId,u.HashContrasena,u.Activo AND r.Activo FROM Usuarios u JOIN Roles r ON r.RolId=u.RolId WHERE u.Correo=@email FOR UPDATE",c,tx);
        q.Parameters.AddWithValue("@email",email.Trim());int id;string hash;bool active;
        using(var r=q.ExecuteReader()){if(!r.Read())return null;id=r.GetInt32(0);hash=r.GetString(1);active=r.GetBoolean(2);}
        Execute(c,tx,"INSERT IGNORE INTO SeguridadUsuarios(UsuarioId) VALUES(@id)",id);
        using var state=new MySqlCommand("SELECT Intentos,COALESCE(BloqueadoHasta>CURRENT_TIMESTAMP,FALSE),BloqueadoHasta IS NOT NULL FROM SeguridadUsuarios WHERE UsuarioId=@id FOR UPDATE",c,tx);state.Parameters.AddWithValue("@id",id);
        int attempts;bool blocked,expired;
        using(var r=state.ExecuteReader()){r.Read();attempts=r.GetInt32(0);blocked=r.GetBoolean(1);expired=r.GetBoolean(2)&&!blocked;}
        if(blocked){tx.Commit();return null;}
        if(!active||!PasswordHasher.Verify(password,hash))
        {
            if(expired)Execute(c,tx,"UPDATE SeguridadUsuarios SET Intentos=0,BloqueadoHasta=NULL WHERE UsuarioId=@id",id);
            Execute(c,tx,"UPDATE SeguridadUsuarios SET Intentos=Intentos+1,BloqueadoHasta=IF(Intentos>=5,CURRENT_TIMESTAMP+INTERVAL 15 MINUTE,NULL) WHERE UsuarioId=@id",id);
            AuditLog.Write(c,tx,"Inicio de sesión fallido","Usuarios",id,id);tx.Commit();return null;
        }
        Execute(c,tx,"UPDATE SeguridadUsuarios SET Intentos=0,BloqueadoHasta=NULL WHERE UsuarioId=@id",id);tx.Commit();
        return AccessControl.AuthenticateCore(email,password);
    }
    public static bool MustChange(int id)
    {
        using var c=Open();using var q=new MySqlCommand("SELECT CambioObligatorio FROM SeguridadUsuarios WHERE UsuarioId=@id",c);q.Parameters.AddWithValue("@id",id);return Convert.ToBoolean(q.ExecuteScalar()??false);
    }
    public static void ValidatePassword(string password){if(password.Length<12||password.Length>128)throw new InvalidOperationException("Usa una contraseña de entre 12 y 128 caracteres.");}
    public static void ChangePassword(string current,string next)
    {
        ValidatePassword(next);var u=AppSession.CurrentUser??throw new UnauthorizedAccessException();
        using var c=Open();using var tx=c.BeginTransaction();using var q=new MySqlCommand("SELECT HashContrasena FROM Usuarios WHERE UsuarioId=@id AND Activo=TRUE FOR UPDATE",c,tx);q.Parameters.AddWithValue("@id",u.Id);
        if(!PasswordHasher.Verify(current,Convert.ToString(q.ExecuteScalar())??""))throw new InvalidOperationException("La contraseña actual no coincide.");
        if(PasswordHasher.Verify(next,Convert.ToString(q.ExecuteScalar())??""))throw new InvalidOperationException("Elige una contraseña diferente.");
        SetPassword(c,tx,u.Id,next,false);AuditLog.Write(c,tx,"Cambiar contraseña propia","Usuarios",u.Id);tx.Commit();
    }
    public static string GenerateRecovery(string current)
    {
        RequireAdmin();int id=AppSession.CurrentUser!.Id;using var c=Open();using var tx=c.BeginTransaction();
        using var q=new MySqlCommand("SELECT HashContrasena FROM Usuarios WHERE UsuarioId=@id FOR UPDATE",c,tx);q.Parameters.AddWithValue("@id",id);
        if(!PasswordHasher.Verify(current,Convert.ToString(q.ExecuteScalar())??""))throw new InvalidOperationException("La contraseña actual no coincide.");
        string code=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        using var save=new MySqlCommand("INSERT INTO SeguridadUsuarios(UsuarioId,RecuperacionHash) VALUES(@id,@hash) ON DUPLICATE KEY UPDATE RecuperacionHash=@hash",c,tx);save.Parameters.AddWithValue("@id",id);save.Parameters.AddWithValue("@hash",PasswordHasher.Hash(code));save.ExecuteNonQuery();
        AuditLog.Write(c,tx,"Renovar código de recuperación","Usuarios",id);tx.Commit();return code;
    }
    public static void Recover(string email,string code,string next)
    {
        ValidatePassword(next);using var c=Open();using var tx=c.BeginTransaction();
        using var q=new MySqlCommand("SELECT u.UsuarioId,s.RecuperacionHash FROM Usuarios u JOIN Roles r ON r.RolId=u.RolId JOIN SeguridadUsuarios s ON s.UsuarioId=u.UsuarioId WHERE u.Correo=@email AND u.Activo=TRUE AND r.Activo=TRUE AND r.Nombre='Administrador' FOR UPDATE",c,tx);q.Parameters.AddWithValue("@email",email.Trim());int id;string hash;
        using(var r=q.ExecuteReader()){if(!r.Read()||r.IsDBNull(1))throw new InvalidOperationException("Correo o código de recuperación incorrectos.");id=r.GetInt32(0);hash=r.GetString(1);}
        if(!PasswordHasher.Verify(code.Trim().ToUpperInvariant(),hash))throw new InvalidOperationException("Correo o código de recuperación incorrectos.");
        SetPassword(c,tx,id,next,false);Execute(c,tx,"UPDATE SeguridadUsuarios SET RecuperacionHash=NULL WHERE UsuarioId=@id",id);
        AuditLog.Write(c,tx,"Recuperar acceso con código de un uso","Usuarios",id,id);tx.Commit();
    }
    public static void SetPassword(MySqlConnection c,MySqlTransaction tx,int id,string password,bool temporary)
    {
        using var q=new MySqlCommand("UPDATE Usuarios SET HashContrasena=@hash WHERE UsuarioId=@id",c,tx);q.Parameters.AddWithValue("@id",id);q.Parameters.AddWithValue("@hash",PasswordHasher.Hash(password));q.ExecuteNonQuery();
        using var s=new MySqlCommand("INSERT INTO SeguridadUsuarios(UsuarioId,CambioObligatorio) VALUES(@id,@temp) ON DUPLICATE KEY UPDATE CambioObligatorio=@temp,Intentos=0,BloqueadoHasta=NULL,RecuperacionHash=NULL",c,tx);s.Parameters.AddWithValue("@id",id);s.Parameters.AddWithValue("@temp",temporary);s.ExecuteNonQuery();
    }
    private static void Execute(MySqlConnection c,MySqlTransaction tx,string sql,int id){using var q=new MySqlCommand(sql,c,tx);q.Parameters.AddWithValue("@id",id);q.ExecuteNonQuery();}
}

internal sealed class PasswordForm : StudioForm
{
    public PasswordForm(bool recovery=false)
    {
        Text=recovery?"Recuperar acceso de administrador":"Cambiar contraseña";Size=new Size(720,510);StartPosition=FormStartPosition.CenterParent;var p=SettingsUi.Panel(this);
        var first=new TextBox{UseSystemPasswordChar=!recovery};var code=new TextBox{UseSystemPasswordChar=true};var next=new TextBox{UseSystemPasswordChar=true};var confirm=new TextBox{UseSystemPasswordChar=true};
        SettingsUi.Field(p,recovery?"Correo del administrador":"Contraseña actual",first);if(recovery)SettingsUi.Field(p,"Código de recuperación de un solo uso",code);
        SettingsUi.Field(p,"Nueva contraseña (12 a 128 caracteres)",next);SettingsUi.Field(p,"Confirmar nueva contraseña",confirm);
        SettingsUi.Action(p,"Guardar nueva contraseña",()=>{if(next.Text!=confirm.Text)throw new InvalidOperationException("Las contraseñas no coinciden.");if(recovery)SecurityService.Recover(first.Text,code.Text,next.Text);else SecurityService.ChangePassword(first.Text,next.Text);MessageBox.Show("Contraseña actualizada.");DialogResult=DialogResult.OK;Close();});
    }
}

internal sealed class ConfigurationForm : StudioForm
{
    public ConfigurationForm()
    {
        SecurityService.RequireAdmin();Text="Configuración y protección de datos";Size=new Size(780,740);StartPosition=FormStartPosition.CenterParent;var p=SettingsUi.Panel(this);
        SettingsUi.Note(p,"Conservación de clientes: el plazo empieza cuando das de baja al cliente, no desde su última asistencia. Se conserva el historial y se anonimizan nombre, correo y teléfono.");
        var enabled=new CheckBox{Text="Activar anonimización automática",AutoSize=true};var days=new NumericUpDown{Minimum=30,Maximum=3650,Value=30};p.Controls.Add(enabled);SettingsUi.Field(p,"Días desde la baja (30 a 3650)",days);
        using(var c=SecurityService.Open())using(var q=new MySqlCommand("SELECT RetencionActiva,DiasRetencion FROM Configuracion WHERE Id=1",c))using(var r=q.ExecuteReader()){r.Read();enabled.Checked=r.GetBoolean(0);days.Value=r.GetInt32(1);}
        SettingsUi.Action(p,"Ver clientes que cumplen el plazo",()=>
        {
            using var c=SecurityService.Open();using var q=new MySqlCommand("SELECT ClienteId,NombreCompleto,FechaInactivacion FROM Clientes WHERE Estado='Inactivo' AND DatosPurgados=FALSE AND FechaInactivacion<=CURRENT_TIMESTAMP-INTERVAL @days DAY",c);q.Parameters.AddWithValue("@days",(int)days.Value);using var r=q.ExecuteReader();var table=new System.Data.DataTable();table.Load(r);
            using var preview=new StudioForm{Text="Vista previa: no se han cambiado datos",Size=new Size(850,500),StartPosition=FormStartPosition.CenterParent};preview.Controls.Add(new DataGridView{Dock=DockStyle.Fill,DataSource=table,ReadOnly=true,AllowUserToAddRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill});preview.ShowDialog(this);
        });
        SettingsUi.Action(p,"Guardar política de conservación",()=>
        {
            SecurityService.RequireAdmin();if(enabled.Checked&&MessageBox.Show(this,"Al activar esta política se anonimizarán de forma irreversible los clientes que cumplan el plazo en la próxima limpieza. Revisa antes la vista previa y guarda un respaldo. ¿Activar?","Conservación",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            using var c=SecurityService.Open();using var q=new MySqlCommand("UPDATE Configuracion SET RetencionActiva=@active,DiasRetencion=@days WHERE Id=1",c);q.Parameters.AddWithValue("@active",enabled.Checked);q.Parameters.AddWithValue("@days",(int)days.Value);AuditLog.Execute(c,q,"Configurar conservación de clientes","Configuracion",1);MessageBox.Show("Política guardada.");
        });
        SettingsUi.Note(p,"Recuperación del administrador: genera un código, guárdalo fuera de la laptop y úsalo si olvidas tu contraseña. Generar uno nuevo invalida el anterior.");
        var current=new TextBox{UseSystemPasswordChar=true};SettingsUi.Field(p,"Contraseña actual para generar el código",current);
        SettingsUi.Action(p,"Generar código de recuperación",()=>
        {
            string code=SecurityService.GenerateRecovery(current.Text);current.Clear();
            using var display=new Form{Text="Guarda este código: solo se muestra ahora",Size=new Size(710,280),StartPosition=FormStartPosition.CenterParent};var content=SettingsUi.Panel(display);SettingsUi.Note(content,"Cópialo a un lugar seguro fuera de esta computadora. No lo incluyas en la carpeta del instalador.");SettingsUi.Field(content,"Código de un solo uso",new TextBox{ReadOnly=true,Text=code});display.ShowDialog(this);
        });
        SettingsUi.Action(p,"Cambiar mi contraseña…",()=>{if(TopLevelControl is MainForm workspace)workspace.OpenWorkspace("Mi contraseña",()=>new PasswordForm(),null);else{using var f=new PasswordForm();f.ShowDialog(this);}});
    }
}





