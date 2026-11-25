namespace IntegraGym;

internal sealed class BackupForm : StudioForm, IWorkspaceActivity
{
    private readonly CheckBox alternate = new() { Text = "Usar otra cuenta de MySQL (por ejemplo, root)", AutoSize = true, Margin = new Padding(12) };
    private readonly TextBox username = new() { Width = 200, Enabled = false, Text = "root", Margin = new Padding(12) };
    private readonly TextBox password = new() { Width = 260, Enabled = false, UseSystemPasswordChar = true, Margin = new Padding(12) };
    private readonly Button create = ModuleUi.Button("Guardar copia de seguridad…", true);
    private readonly Button cancel = ModuleUi.Button("Cancelar respaldo", false);
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(850, 0), Margin = new Padding(12), ForeColor = Color.DimGray };
    private readonly ProgressBar progress = new() { Width = 650, Height = 20, Margin = new Padding(12), Visible = false, Style = ProgressBarStyle.Marquee };
    private CancellationTokenSource? cancellation;
    private bool running;
    public bool IsBusy => running;
    private string? executable;

    public BackupForm()
    {
        AppSession.Require("respaldos.crear");
        var content = ModuleUi.Build(this, "Copia de seguridad", "Guarda una copia completa de la base del gimnasio en tu computadora o una memoria USB.");
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.White, Padding = new Padding(16) };
        var settings = new MySqlConnector.MySqlConnectionStringBuilder(DatabaseSettings.ConnectionString);
        layout.Controls.Add(new Label { Text = $"Base: {settings.Database} · Servidor: {settings.Server}:{settings.Port}", AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold), Margin = new Padding(12) });
        layout.Controls.Add(new Label { Text = "Incluye tablas, registros, procedimientos, funciones, eventos y disparadores. Las cuentas de MySQL se configuran aparte al reinstalar el servidor.", AutoSize = true, MaximumSize = new Size(850, 0), Margin = new Padding(12) });
        layout.Controls.Add(alternate);
        var credentials = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        credentials.Controls.Add(ModuleUi.Caption("Usuario de MySQL")); credentials.Controls.Add(username);
        credentials.Controls.Add(ModuleUi.Caption("Contraseña de MySQL")); credentials.Controls.Add(password);
        layout.Controls.Add(credentials);
        layout.Controls.Add(new Label { Text = "Esta cuenta es del servidor MySQL. Su contraseña se utiliza solo durante el respaldo y no se guarda en la configuración del sistema.", AutoSize = true, MaximumSize = new Size(850, 0), Margin = new Padding(12), ForeColor = Color.DimGray });
        var options = ModuleUi.Button("Seleccionar mysqldump.exe…", false);
        options.Click += (_, _) => { using var dialog = new OpenFileDialog { Filter = "Herramienta MySQL (mysqldump.exe)|mysqldump.exe", Title = "Selecciona mysqldump.exe en la carpeta bin de MySQL Server" }; if (dialog.ShowDialog(this) == DialogResult.OK) { executable = dialog.FileName; status.Text = "Herramienta seleccionada."; } };
        layout.Controls.Add(options);
        if (AppSession.CurrentUser?.RoleName == "Administrador") { var center = ModuleUi.Button("Programar y verificar copias…", false); center.Click += (_, _) => SettingsUi.Try(this, () => { if (TopLevelControl is MainForm workspace) workspace.OpenWorkspace("Programar respaldos", () => new BackupCenterForm(), "respaldos.crear"); else { using var f = new BackupCenterForm(); f.ShowDialog(this); } }); layout.Controls.Add(center); }
        var actions = new FlowLayoutPanel { AutoSize = true }; actions.Controls.Add(create); actions.Controls.Add(cancel); layout.Controls.Add(actions);
        layout.Controls.Add(progress); layout.Controls.Add(status); content.Controls.Add(layout);
        status.Text = "Elige dónde guardar el archivo .sql. Si la cuenta de la aplicación no tiene permisos, utiliza una cuenta MySQL autorizada.";
        cancel.Enabled = false;
        alternate.CheckedChanged += (_, _) => { username.Enabled = alternate.Checked; password.Enabled = alternate.Checked; };
        alternate.Checked = true;
        create.Click += async (_, _) => await CreateBackup(); cancel.Click += (_, _) => cancellation?.Cancel();
        FormClosing += (_, e) => { if (running) { e.Cancel = true; cancellation?.Cancel(); status.Text = "Cancelando el respaldo y eliminando el archivo incompleto…"; } };
        AppUi.StyleForm(this);
    }

    private async Task CreateBackup()
    {
        if (alternate.Checked && string.IsNullOrWhiteSpace(username.Text)) { MessageBox.Show(this, "Escribe el usuario de MySQL autorizado."); return; }
        using var dialog = new SaveFileDialog { Filter = "Respaldo SQL (*.sql)|*.sql", AddExtension = true, DefaultExt = "sql", OverwritePrompt = true, FileName = $"IntegraGym-respaldo-{DateTime.Now:yyyy-MM-dd-HHmmss}.sql" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        running = true; cancellation = new CancellationTokenSource();
        create.Enabled = false; alternate.Enabled = false; username.Enabled = false; password.Enabled = false;
        cancel.Enabled = true; progress.Visible = true; status.Text = "Creando la copia completa. Espera a la confirmación antes de retirar la memoria USB…";
        try
        {
            var result = await BackupService.CreateAsync(dialog.FileName, alternate.Checked ? username.Text : null,
                alternate.Checked ? password.Text : null, executable, cancellation.Token);
            status.Text = "Copia guardada: " + result.FilePath + (result.AuditWarning is null ? "" : "\n" + result.AuditWarning);
            MessageBox.Show(this, status.Text, "Respaldo terminado", MessageBoxButtons.OK, result.AuditWarning is null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException) { status.Text = "Respaldo cancelado. El archivo anterior se conservó."; }
        catch (Exception ex) when (ex is MySqlConnector.MySqlException or IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            status.Text = "El respaldo no se completó."; MessageBox.Show(this, ex.Message, "Copia de seguridad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            password.Clear(); running = false; progress.Visible = false; cancellation.Dispose(); cancellation = null;
            create.Enabled = true; alternate.Enabled = true; username.Enabled = alternate.Checked; password.Enabled = alternate.Checked; cancel.Enabled = false;
        }
    }
}




