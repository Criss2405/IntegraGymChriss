using MySqlConnector;
using System.Data;

namespace IntegraGym;

internal static class ModuleUi
{
    public static Panel Build(Form form, string title, string description)
    {
        form.Text = "Integra Gym | " + title;
        form.ClientSize = new Size(1040, 650);
        form.MinimumSize = new Size(850, 560);
        form.StartPosition = FormStartPosition.CenterParent;
        form.WindowState = FormWindowState.Maximized;
        var header = new Panel { Dock = DockStyle.Top, Height = 110, Padding = new Padding(24) };
        header.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI", 22, FontStyle.Bold), AutoSize = true, Location = new Point(24, 18) });
        header.Controls.Add(new Label { Text = description, AutoSize = true, Location = new Point(27, 67), ForeColor = Color.DimGray });
        var back = Button("Volver al inicio", false);
        back.Dock = DockStyle.Right;
        back.Width = 140;
        back.Click += (_, _) => form.Close();
        header.Controls.Add(back);
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 0, 24, 24) };
        form.Controls.Add(content);
        form.Controls.Add(header);
        return content;
    }

    public static Button Button(string text, bool primary) => new()
    {
        Text = text, AutoSize = true, MinimumSize = new Size(120, 36), Padding = new Padding(8, 3, 8, 3),
        FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(57,97,170) : Color.White,
        ForeColor = primary ? Color.White : Color.FromArgb(26,39,57), Margin = new Padding(8)
    };

    public static DateTimePicker Date(DateTime value) => new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 135, Value = value, Margin = new Padding(8) };
    public static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(8, 14, 0, 0) };
    public static DataGridView Grid()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
        AppUi.ConfigureGrid(grid);
        return grid;
    }
    public static DataTable Read(MySqlCommand command)
    {
        using var reader = command.ExecuteReader();
        var table = new DataTable(); table.Load(reader); return table;
    }
}

internal sealed class AttendanceForm : StudioForm
{
    private readonly ComboBox client = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 310, Margin = new Padding(8), AutoCompleteSource = AutoCompleteSource.ListItems, AutoCompleteMode = AutoCompleteMode.SuggestAppend };
    private readonly DateTimePicker from = ModuleUi.Date(DateTime.Today);
    private readonly DateTimePicker through = ModuleUi.Date(DateTime.Today);
    private readonly CheckBox selectedOnly = new() { Text = "Solo el cliente seleccionado", AutoSize = true, Margin = new Padding(8, 14, 8, 0) };
    private readonly DataGridView grid = ModuleUi.Grid();
    private readonly HistoryNavigation pages = new();
    private readonly Label membership = new() { AutoSize = true, ForeColor = Color.DimGray, Dock = DockStyle.Top, Height = 40, Padding = new Padding(8) };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(8) };
    private readonly Button register = ModuleUi.Button("Registrar entrada", true);

    public AttendanceForm()
    {
        AppSession.Require("asistencias.gestionar");
        var content = ModuleUi.Build(this, "Asistencias", "Registra una entrada diaria por cliente con membresía vigente y consulta su historial.");
        var entry = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Color.White, Padding = new Padding(8) };
        entry.Controls.Add(ModuleUi.Caption("Cliente")); entry.Controls.Add(client); entry.Controls.Add(register);
        var reload = ModuleUi.Button("Actualizar clientes", false); entry.Controls.Add(reload);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 8, 0, 8) };
        filters.Controls.Add(ModuleUi.Caption("Desde")); filters.Controls.Add(from); filters.Controls.Add(ModuleUi.Caption("Hasta")); filters.Controls.Add(through); filters.Controls.Add(selectedOnly);
        var search = ModuleUi.Button("Consultar", false); filters.Controls.Add(search);
        content.Controls.Add(grid); content.Controls.Add(pages); content.Controls.Add(status); content.Controls.Add(filters); content.Controls.Add(membership); content.Controls.Add(entry);
        register.Click += (_, _) => Register(); search.Click += (_, _) => LoadEntries(); reload.Click += (_, _) => LoadClients();
        client.SelectedValueChanged += (_, _) => { ShowMembership(); pages.Reset(); };
        pages.Navigate += page => LoadEntries(page, false);
        from.ValueChanged += (_, _) => pages.Reset(); through.ValueChanged += (_, _) => pages.Reset(); selectedOnly.CheckedChanged += (_, _) => pages.Reset();
        Load += (_, _) => { LoadClients(); LoadEntries(); };
        AppUi.StyleForm(this);
    }

    private void LoadClients()
    {
        try
        {
            using var connection = new MySqlConnection(DatabaseSettings.ConnectionString); connection.Open();
            using var command = new MySqlCommand("SELECT ClienteId,NombreCompleto FROM Clientes WHERE Estado='Activo' AND DatosPurgados=FALSE ORDER BY NombreCompleto", connection);
            client.DisplayMember = "NombreCompleto"; client.ValueMember = "ClienteId";
            client.DataSource = ModuleUi.Read(command); client.SelectedIndex = -1; ShowMembership();
        }
        catch (MySqlException ex) { ShowError(ex.Message); }
    }
    private void ShowMembership()
    {
        register.Enabled = false;
        if (client.SelectedValue is not int id) { membership.Text = "Selecciona al cliente que llegó al gimnasio."; return; }
        try
        {
            using var connection = new MySqlConnection(DatabaseSettings.ConnectionString); connection.Open();
            using var command = new MySqlCommand("SELECT p.Nombre,m.FechaVencimiento,m.EstadoPago FROM Membresias m JOIN Planes p ON p.PlanId=m.PlanId WHERE m.ClienteId=@id AND m.Estado='Activa' AND m.FechaInicio<=CURDATE() AND m.FechaVencimiento>=CURDATE() ORDER BY m.FechaVencimiento DESC LIMIT 1", connection);
            command.Parameters.AddWithValue("@id", id);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) { membership.Text = "Sin membresía vigente. Registra o renueva su membresía para permitir la entrada."; return; }
            membership.Text = $"Plan: {reader.GetString(0)} · Vence: {reader.GetDateTime(1):dd/MM/yyyy} · Pago: {reader.GetString(2)}";
            register.Enabled = true;
        }
        catch (MySqlException ex) { ShowError(ex.Message); }
    }
    private void Register()
    {
        if (client.SelectedValue is not int id) return;
        try { GymOperations.RegisterAttendance(id); LoadEntries(); status.Text = "Entrada registrada correctamente para " + client.Text + "."; }
        catch (Exception ex) when (ex is MySqlException or InvalidOperationException or UnauthorizedAccessException) { ShowError(ex.Message); }
    }
    private void LoadEntries(long page = 0, bool reset = true)
    {
        if (from.Value.Date > through.Value.Date) { ShowError("La fecha inicial debe ser anterior o igual a la final."); return; }
        if (selectedOnly.Checked && client.SelectedValue is not int) { ShowError("Selecciona un cliente para filtrar su historial."); return; }
        try
        {
            var result = HistoryPages.Read(false, from.Value, through.Value, "", selectedOnly.Checked ? (int?)client.SelectedValue : null, page, reset ? null : pages.Snapshot);
            grid.DataSource = result.Rows; pages.Display(result);
            grid.Columns["FechaHora"]!.DefaultCellStyle.Format = "dd/MM/yyyy HH:mm:ss";
            status.Text = $"{result.Total:N0} entradas en el periodo. El historial completo sigue guardado en MySQL.";
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { pages.Reset(); grid.DataSource = null; status.Text = "No se pudo consultar el historial."; ShowError(ex.Message); }
    }
    private void ShowError(string message) => MessageBox.Show(this, message, "Asistencias", MessageBoxButtons.OK, MessageBoxIcon.Information);
}

internal sealed class AuditForm : StudioForm
{
    private readonly DateTimePicker from = ModuleUi.Date(DateTime.Today.AddDays(-30));
    private readonly DateTimePicker through = ModuleUi.Date(DateTime.Today);
    private readonly TextBox search = new() { Width = 240, Margin = new Padding(8) };
    private readonly DataGridView grid = ModuleUi.Grid();
    private readonly HistoryNavigation pages = new();
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(8) };

    public AuditForm()
    {
        AppSession.Require("bitacora.ver");
        var content = ModuleUi.Build(this, "Bitácora", "Consulta quién realizó cada acción, el registro afectado y cuándo ocurrió.");
        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(8), BackColor = Color.White };
        filters.Controls.Add(ModuleUi.Caption("Desde")); filters.Controls.Add(from); filters.Controls.Add(ModuleUi.Caption("Hasta")); filters.Controls.Add(through);
        filters.Controls.Add(ModuleUi.Caption("Usuario o acción")); filters.Controls.Add(search);
        var refresh = ModuleUi.Button("Consultar", true); filters.Controls.Add(refresh); refresh.Click += (_, _) => LoadLog();
        content.Controls.Add(grid); content.Controls.Add(pages); content.Controls.Add(status); content.Controls.Add(filters);
        pages.Navigate += page => LoadLog(page, false);
        from.ValueChanged += (_, _) => pages.Reset(); through.ValueChanged += (_, _) => pages.Reset(); search.TextChanged += (_, _) => pages.Reset();
        Load += (_, _) => LoadLog(); AppUi.StyleForm(this);
    }
    private void LoadLog(long page = 0, bool reset = true)
    {
        if (from.Value.Date > through.Value.Date) { MessageBox.Show(this, "Revisa el intervalo de fechas."); return; }
        try
        {
            var result = HistoryPages.Read(true, from.Value, through.Value, search.Text, null, page, reset ? null : pages.Snapshot);
            grid.DataSource = result.Rows; pages.Display(result);
            grid.Columns["FechaHora"]!.DefaultCellStyle.Format = "dd/MM/yyyy HH:mm:ss";
            status.Text = $"{result.Total:N0} acciones en el periodo. Los registros no se borran al superar 5,000.";
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { pages.Reset(); grid.DataSource = null; status.Text = "No se pudo consultar la bitácora."; MessageBox.Show(this, ex.Message, "Bitácora", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}



