using MySqlConnector;

namespace IntegraGym;

internal sealed class MainForm : Form
{
    private readonly AppUser currentUser;
    private readonly Panel body = new();
    private readonly Label greeting = new();
    private readonly System.Windows.Forms.Timer cleanupTimer = new() { Interval = 60 * 60 * 1000 };
    private bool cleanupErrorShown;
    private readonly Dictionary<string, EmbeddedModuleHost> modules = new();
    private readonly List<FairButton> navButtons = new();
    private bool closingWorkspace;
    private Action? shellLook;

    public bool LogoutRequested { get; private set; }

    public MainForm(AppUser user)
    {
        currentUser = user;
        Text = "Integra Gym | Sistema de gestión";
        MinimumSize = new Size(980, 650);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(244,247,251);
        BuildLayout();
        Font = new Font("Segoe UI", 10);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        ShowHome();
        cleanupTimer.Tick += (_, _) => RunRetentionCleanup();
        Load += (_, _) => { RunRetentionCleanup(); cleanupTimer.Start(); };
        FormClosed += (_, _) => cleanupTimer.Dispose();
    }

    private void BuildLayout()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 244, BackColor = Color.FromArgb(8,23,44), Padding = new Padding(18, 24, 18, 16) };
        var brand = new Label { Text = "         Integra Gym", Font = new Font("Segoe UI", 15, FontStyle.Bold), Dock = DockStyle.Top, Height = 58, ForeColor = Color.White };
        brand.Paint+=(_,e)=>{var box=new Rectangle(1,1,39,39);using var p=new Pen(DashboardArt.Cyan);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;e.Graphics.DrawRoundedRectangle(p,box,new Size(8,8));TextRenderer.DrawText(e.Graphics,"IG",brand.Font,box,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);};
        var section = new Label { Text = "ESPACIO DE TRABAJO", Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = Color.FromArgb(150,170,190), Dock = DockStyle.Top, Height = 30 };

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.FromArgb(8,23,44)
        };
        DashboardArt.DarkScroll(navigation); navigation.SizeChanged+=(_,_)=>{foreach(var nav in navButtons)nav.Width=Math.Max(120,navigation.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-2);};
        AddNav(navigation, "Dashboard", null, ShowHome);
        AddNav(navigation, "Clientes", "clientes.gestionar", () => OpenWorkspace("clientes.gestionar", () => new ClientsForm(), "clientes.gestionar"));
        AddNav(navigation, "Planes", "planes.gestionar", () => OpenWorkspace("planes.gestionar", () => new PlansForm(), "planes.gestionar"));
        AddNav(navigation, "Membresías", "membresias.gestionar", () => OpenWorkspace("membresias.gestionar", () => new MembershipsForm(), "membresias.gestionar"));
        AddNav(navigation, "Pagos", "pagos.gestionar", () => OpenWorkspace("pagos.gestionar", () => new PaymentsForm(), "pagos.gestionar"));
        AddNav(navigation, "Asistencias", "asistencias.gestionar", () => OpenWorkspace("asistencias.gestionar", () => new AttendanceForm(), "asistencias.gestionar"));
        AddNav(navigation, "Entrenadores", "entrenadores.gestionar", () => OpenWorkspace("entrenadores.gestionar", () => new TrainersForm(), "entrenadores.gestionar"));
        AddNav(navigation, "Reportes", "reportes.ver", () => OpenWorkspace("reportes.ver", () => new ReportsForm(), "reportes.ver"));
        AddNav(navigation, "Usuarios y permisos", "usuarios.gestionar", () => OpenWorkspace("usuarios.gestionar", () => new UsersForm(), "usuarios.gestionar"));
        AddNav(navigation, "Bitácora", "bitacora.ver", () => OpenWorkspace("bitacora.ver", () => new AuditForm(), "bitacora.ver"));
        AddNav(navigation, "Copia de seguridad", "respaldos.crear", () => OpenWorkspace("respaldos.crear", () => new BackupForm(), "respaldos.crear"));
        if (currentUser.RoleName == "Administrador") AddNav(navigation, "Configuración", null, () => OpenWorkspace("Configuración", () => new ConfigurationForm(), null));
        AddNav(navigation, "Mi contraseña", null, () => OpenWorkspace("Mi contraseña", () => new PasswordForm(), null));
        sidebar.Controls.Add(navigation);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 36 };
        footer.Controls.Add(new Label { Text = "Aplicación local · v1.4.1", Dock = DockStyle.Bottom, Height = 24, ForeColor = Color.Gray });
        sidebar.Controls.Add(footer); sidebar.Controls.Add(section); sidebar.Controls.Add(brand);

        var mainArea = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(244,247,251) };
        var top = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(7,22,41) };
        var breadcrumb = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(16, 17, 0, 0) };
        breadcrumb.Controls.Add(new Label { Text = "Integra Gym   /", ForeColor = Night.Muted, AutoSize = true });
        greeting.ForeColor=Color.White; greeting.Text = "Dashboard"; greeting.AutoSize = true; greeting.Font = new Font("Segoe UI", 9, FontStyle.Bold); breadcrumb.Controls.Add(greeting);
        var account = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 380, WrapContents = false, Padding = new Padding(0, 10, 8, 0) };
        var identity = new Label { Text = currentUser.Name + " · " + currentUser.RoleName, AutoSize = false, Size = new Size(205, 34), TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true, ForeColor = Night.Muted };
        account.Controls.Add(identity);
        var logout = new FairButton { Text = "Cerrar sesión", Size = new Size(140, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.White, Cursor = Cursors.Hand };
        logout.Click += (_, _) => { if (HasBusyModule()) { NotifyBusy(); return; } LogoutRequested = true; Close(); };
        account.Controls.Add(logout); top.Controls.Add(breadcrumb); top.Controls.Add(account);
        body.Dock = DockStyle.Fill; body.Padding = new Padding(30); mainArea.Controls.Add(body); mainArea.Controls.Add(top);
        Controls.Add(mainArea); Controls.Add(sidebar);
        var labels=new[]{brand,section,greeting,identity,(Label)breadcrumb.Controls[0],(Label)footer.Controls[0]};
        var pt=new float[]{16,10.5f,11,11,11,10};var weights=new[]{2,1,2,0,0,0};
        shellLook=()=>{for(int i=0;i<labels.Length;i++)labels[i].Font=DashboardLook.Face(pt[i],weights[i]);
            sidebar.Width=LogicalToDeviceUnits(292);
            logout.Width=LogicalToDeviceUnits(164);
            identity.Width=Math.Min(LogicalToDeviceUnits(370),Math.Max(LogicalToDeviceUnits(210),TextRenderer.MeasureText(identity.Text,identity.Font).Width+12));
            account.Width=identity.Width+logout.Width+LogicalToDeviceUnits(30);
            foreach(var nav in navButtons)nav.Width=Math.Max(120,navigation.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-2);
            section.Text="Espacio de trabajo";
            foreach(var b in navButtons.Append(logout)){b.Soft=true;b.Invalidate();}};

    }

    private void AddNav(Control parent, string text, string? permission, Action action)
    {
        if (permission is not null && !currentUser.Can(permission)) return;
        var button = new FairButton
        {
            Text = text, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat,
            Idle = Color.FromArgb(8,23,44), ForeColor = Color.FromArgb(208,219,231),
            Size = new Size(198, 41), Margin = new Padding(0, 0, 0, 5), Cursor = Cursors.Hand
        };
        if(text == "Dashboard") button.SelectedItem = true;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Theme.Selected;
        button.Tag = permission ?? text; navButtons.Add(button);
        button.Click += (_, _) => { if (HasBusyModule()) { NotifyBusy(); return; } action(); };
        parent.Controls.Add(button);
    }
    private bool HasBusyModule() => modules.Values.Any(host => host.Module is IWorkspaceActivity { IsBusy: true });
    private void NotifyBusy() => MessageBox.Show(this, "Espera a que termine la operación de respaldo o cancélala desde su módulo antes de cambiar de pantalla o cerrar sesión.", "Operación en curso", MessageBoxButtons.OK, MessageBoxIcon.Information);
    private void SelectNavigation(string key)
    {
        shellLook?.Invoke();
        foreach(var button in navButtons)button.SelectedItem=Equals(button.Tag,key);
    }
    private void HideContent()
    {
        foreach(Control control in body.Controls.Cast<Control>().ToArray())
        {
            control.Hide();
            if(control is DashboardView){body.Controls.Remove(control);control.Dispose();}
        }
    }
    public void OpenWorkspace(string key, Func<Form> create, string? permission)
    {
        if(HasBusyModule()){NotifyBusy();return;}
        try
        {
            if(permission is not null)AppSession.Require(permission);
            if(key=="Configuración" || key=="Programar respaldos")SecurityService.RequireAdmin();
            if(!modules.TryGetValue(key,out var host))
            {
                var module=create();
                host=new EmbeddedModuleHost(module);modules.Add(key,host);body.Controls.Add(host);
                module.FormClosed+=(_,_)=>
                {
                    modules.Remove(key);
                    if(!closingWorkspace&&!IsDisposed&&IsHandleCreated)
                        BeginInvoke(new Action(()=>{body.Controls.Remove(host);host.Dispose();ShowHome();}));
                };
            }
            HideContent();body.BackColor=Night.Canvas;body.Padding=new Padding(12);host.Show();host.BringToFront();host.ActivateModule();
            greeting.Text=host.Module.Text.Replace("Integra Gym | ","");SelectNavigation(key=="Programar respaldos"?"respaldos.crear":key);
        }
        catch(Exception ex) when(ex is UnauthorizedAccessException or MySqlException)
        {MessageBox.Show(this,ex.Message,"No se pudo abrir el módulo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }
    private void ShowHome()
    {
        if(HasBusyModule()){NotifyBusy();return;}
        HideContent();body.BackColor=DashboardLook.Canvas;body.Padding=new Padding(18);greeting.Text="Dashboard";SelectNavigation("Dashboard");
        var dashboard=new DashboardView(currentUser.Id,key=>navButtons.FirstOrDefault(b=>Equals(b.Tag,key))?.PerformClick()){Dock=DockStyle.Fill};body.Controls.Add(dashboard);dashboard.BringToFront();
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if(HasBusyModule()){e.Cancel=true;LogoutRequested=false;NotifyBusy();return;}
        base.OnFormClosing(e);if(e.Cancel){LogoutRequested=false;return;}
        closingWorkspace=true;
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){closingWorkspace=true;cleanupTimer.Dispose();foreach(var host in modules.Values.ToArray())host.Dispose();modules.Clear();}
        base.Dispose(disposing);
    }
    private void RunRetentionCleanup()
    {
        var connectionString = DatabaseSettings.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        try
        {
            using var connection = new MySqlConnection(connectionString); connection.Open();
            using var command = new MySqlCommand("CALL sp_PurgarClientesInactivos()", connection); command.ExecuteNonQuery();
            cleanupErrorShown = false;
        }
        catch (MySqlException ex)
        {
            if (cleanupErrorShown) return;
            cleanupErrorShown = true;
            MessageBox.Show("No se pudo ejecutar la limpieza automática. Confirma que aplicaste la migración 001_client_retention.sql en MySQL.\n\n" + ex.Message, "Mantenimiento de clientes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}


















