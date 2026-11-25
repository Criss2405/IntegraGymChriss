using MySqlConnector;
using System.Reflection;
using System.Text;

namespace IntegraGym;

internal static class SchemaUpgrade
{
    public static bool Ready(MySqlConnection c)
    {
        using var q=new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN ('Configuracion','SeguridadUsuarios','Migraciones')",c);
        if(Convert.ToInt32(q.ExecuteScalar())!=3)return false;
        using var version=new MySqlCommand("SELECT COUNT(*) FROM Migraciones WHERE Version=2",c);return Convert.ToInt32(version.ExecuteScalar())==1;
    }
    public static void Apply(MySqlConnection c)
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("IntegraGym.Upgrade.sql")!;
        using var reader=new StreamReader(stream); string delimiter=";"; var sql=new StringBuilder();
        while(reader.ReadLine() is string line)
        {
            if(line.TrimStart().StartsWith("--")) continue;
            if(line.StartsWith("DELIMITER ")) { delimiter=line[10..].Trim(); continue; }
            sql.AppendLine(line);
            if(line.TrimEnd().EndsWith(delimiter,StringComparison.Ordinal))
            {
                string statement=sql.ToString().TrimEnd(); statement=statement[..^delimiter.Length];
                using var q=new MySqlCommand(statement,c); q.ExecuteNonQuery(); sql.Clear();
            }
        }
    }
}

internal sealed class SetupForm : StudioForm
{
    private readonly TextBox host=new(){Text="127.0.0.1"}, database=new(){Text="integra_gym"}, user=new(){Text="integra_app"}, password=new(){UseSystemPasswordChar=true};
    private readonly NumericUpDown port=new(){Minimum=1,Maximum=65535,Value=3306};
    private readonly Label status=new(){AutoSize=true,MaximumSize=new Size(620,0)};
    public SetupForm()
    {
        Text="Integra Gym | Conectar con MySQL"; Size=new Size(720,640); MinimumSize=Size; StartPosition=FormStartPosition.CenterScreen;
        var panel=SettingsUi.Panel(this);
        SettingsUi.Note(panel,"Configura la conexión local. MySQL debe estar instalado y el script inicial ejecutado. La contraseña se guardará cifrada para tu usuario de Windows.");
        SettingsUi.Field(panel,"Servidor local",host); SettingsUi.Field(panel,"Puerto",port); SettingsUi.Field(panel,"Base de datos",database);
        SettingsUi.Field(panel,"Usuario MySQL de la aplicación",user); SettingsUi.Field(panel,"Contraseña MySQL",password);
        try { var b=new MySqlConnectionStringBuilder(DatabaseSettings.ConnectionString); if(b.Server.Length>0)host.Text=b.Server; if(b.Database.Length>0)database.Text=b.Database; if(b.UserID.Length>0)user.Text=b.UserID; port.Value=b.Port; } catch { }
        SettingsUi.Action(panel,"Probar conexión",()=>Test(false)); SettingsUi.Action(panel,"Guardar y continuar",()=>Test(true));
        SettingsUi.Action(panel,"Actualizar una base existente a v1.2…",()=>
        {
            using var credentials=new CredentialsForm("Actualizar estructura", "Cuenta administradora de MySQL (por ejemplo root). La actualización conserva registros y desactiva la limpieza hasta que la configures.");
            if(credentials.ShowDialog(this)!=DialogResult.OK)return;
            SettingsUi.Try(this,()=>{ var b=Builder(); b.UserID=credentials.Account; b.Password=credentials.Secret; using var c=new MySqlConnection(b.ConnectionString); c.Open(); SchemaUpgrade.Apply(c); status.Text="Estructura actualizada. Ahora prueba y guarda con la cuenta de la aplicación."; });
        }); panel.Controls.Add(status);
    }
    private MySqlConnectionStringBuilder Builder()
    {
        if(host.Text.Trim() is not ("localhost" or "127.0.0.1" or "::1")) throw new InvalidOperationException("Esta edición configura MySQL local. Usa localhost o 127.0.0.1.");
        if(user.Text.Trim().Equals("root",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Usa una cuenta de aplicación, no root, para guardar la conexión.");
        return new(){Server=host.Text.Trim(),Port=(uint)port.Value,Database=database.Text.Trim(),UserID=user.Text.Trim(),Password=password.Text,SslMode=MySqlSslMode.None,ConnectionTimeout=5};
    }
    private void Test(bool save)=>SettingsUi.Try(this,()=>
    {
        var b=Builder(); using var c=new MySqlConnection(b.ConnectionString); c.Open();
        if(!SchemaUpgrade.Ready(c))throw new InvalidOperationException("Falta actualizar la estructura. Usa el botón de actualización con una cuenta administradora de MySQL.");
        using var q=new MySqlCommand("SELECT UsuarioId FROM Usuarios LIMIT 1",c); q.ExecuteScalar();
        status.Text="Conexión y estructura correctas.";
        if(save){LocalSecrets.Save("connection.bin",b.ConnectionString); DialogResult=DialogResult.OK; Close();}
    });
}

internal static class SettingsUi
{
    public static FlowLayoutPanel Panel(Form form)
    {
        form.Font=new Font("Segoe UI",10);form.MinimumSize=new Size(700,400);
        var p=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(22),BackColor=Color.FromArgb(244,247,251)};
        form.Controls.Add(p);p.Controls.Add(new Label{Text=form.Text.Replace("Integra Gym | ",""),Font=new Font("Segoe UI",20,FontStyle.Bold),ForeColor=Color.FromArgb(57,97,170),AutoSize=true,MaximumSize=new Size(640,0),Margin=new Padding(4,0,4,18)});return p;
    }
    public static void Note(Control p,string text)=>p.Controls.Add(new Label{Text=text,AutoSize=true,MaximumSize=new Size(640,0),Margin=new Padding(4,6,4,12)});
    public static void Field(Control p,string text,Control input){Note(p,text);input.Width=560;input.Margin=new Padding(4,0,4,6);p.Controls.Add(input);}
    public static Button Action(Control p,string text,Action action)
    {
        var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(240,38),Margin=new Padding(4,6,4,4),FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=Color.FromArgb(57,97,170),Padding=new Padding(8,2,8,2)};b.Click+=(_,_)=>Try(p.FindForm()!,action);p.Controls.Add(b);return b;
    }
    public static void Try(Form form,Action action){try{action();}catch(Exception ex){MessageBox.Show(form,ex.Message,"Integra Gym",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
}
internal sealed class CredentialsForm : StudioForm
{
    private readonly TextBox account=new(){Text="root"},secret=new(){UseSystemPasswordChar=true};
    public string Account=>account.Text.Trim(); public string Secret=>secret.Text;
    public CredentialsForm(string title,string description)
    {
        Text=title;Size=new Size(700,390);StartPosition=FormStartPosition.CenterParent;
        var p=SettingsUi.Panel(this);SettingsUi.Note(p,description);SettingsUi.Field(p,"Usuario de MySQL",account);SettingsUi.Field(p,"Contraseña (no se conservará)",secret);
        SettingsUi.Action(p,"Continuar",()=>{DialogResult=DialogResult.OK;Close();});
    }
}


