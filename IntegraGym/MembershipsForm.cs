using MySqlConnector;
using System.Data;
using System.Globalization;

namespace IntegraGym;

internal sealed class MembershipsForm : StudioForm
{
    private static string ConnectionString => DatabaseSettings.ConnectionString;
    private readonly ComboBox clientCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox planCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker startDate = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy" };
    private readonly NumericUpDown agreedPrice = new() { Minimum = 0, Maximum = 1000000, DecimalPlaces = 2, ThousandsSeparator = true };
    private readonly Label preview = new() { AutoSize = true, ForeColor = Color.FromArgb(57,97,170) };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly DataGridView grid = new();
    private readonly Button cancelButton = new() { Text = "Cancelar membresía", Size = new Size(145, 30), FlatStyle = FlatStyle.Flat };
    private int? selectedMembershipId;
    private string selectedState = "";

    public MembershipsForm()
    {
        Text = "Integra Gym | Membresías"; MinimumSize = new Size(1040, 690); StartPosition = FormStartPosition.CenterParent; WindowState = FormWindowState.Maximized; BackColor = Color.FromArgb(244,247,251);
        BuildLayout(); AppUi.StyleForm(this);
        Load += (_, _) => { LoadCatalogs(); LoadMemberships(); };
    }

    private void BuildLayout()
    {
        Controls.Add(new Label { Text = "Membresías", Font = new Font("Segoe UI", 22, FontStyle.Bold), AutoSize = true, Location = new Point(28, 18) });
        Controls.Add(new Label { Text = "Asigna planes a clientes y consulta su vigencia e historial.", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(31, 58) });
        var editor = new Panel { Location = new Point(28, 90), Size = new Size(950, 158), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.White };
        AddLabel(editor, "Cliente activo", 15, 13); clientCombo.Location = new Point(15, 39); clientCombo.Width = 245; editor.Controls.Add(clientCombo);
        AddLabel(editor, "Plan", 280, 13); planCombo.Location = new Point(280, 39); planCombo.Width = 225; editor.Controls.Add(planCombo);
        AddLabel(editor, "Inicio", 525, 13); startDate.Location = new Point(525, 39); startDate.Width = 120; editor.Controls.Add(startDate);
        AddLabel(editor, "Precio acordado (MXN)", 665, 13); agreedPrice.Location = new Point(665, 39); agreedPrice.Width = 150; editor.Controls.Add(agreedPrice);
        preview.Location = new Point(16, 91); editor.Controls.Add(preview);
        var save = MakeButton("Asignar membresía", 820, 35, true); save.Size = new Size(120, 36); save.Click += (_, _) => SaveMembership(); editor.Controls.Add(save);
        var clear = MakeButton("Limpiar", 820, 80, false); clear.Click += (_, _) => ClearForm(); editor.Controls.Add(clear);
        Controls.Add(editor);
        planCombo.SelectedIndexChanged += (_, _) => { LoadPlanPrice(); UpdatePreview(); }; startDate.ValueChanged += (_, _) => UpdatePreview();
        grid.Location = new Point(28, 270); grid.Size = new Size(950, 285); grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grid.ReadOnly = true; AppUi.ConfigureGrid(grid); grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.AllowUserToAddRows = false; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None; grid.SelectionChanged += (_, _) => SelectMembership(); Controls.Add(grid);
        var refresh = MakeButton("Actualizar lista", 28, 570, false); refresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; refresh.Click += (_, _) => LoadMemberships(); Controls.Add(refresh);
        var catalogs = MakeButton("Actualizar clientes y planes", 300, 570, false); catalogs.Width = 200; catalogs.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; catalogs.Click += (_, _) => { if (MessageBox.Show(this, "Se recargarán los clientes y planes y se restablecerá el precio del plan seleccionado. ¿Continuar?", "Actualizar catálogos", MessageBoxButtons.YesNo) == DialogResult.Yes) LoadCatalogs(); }; Controls.Add(catalogs);
        cancelButton.Enabled = false; cancelButton.Location = new Point(140, 570); cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; cancelButton.Click += (_, _) => CancelMembership(); Controls.Add(cancelButton);
        status.Location = new Point(515, 578); status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; Controls.Add(status);
    }

    private static void AddLabel(Control parent, string text, int x, int y) => parent.Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = Color.FromArgb(102,117,136) });

    private static Button MakeButton(string text, int x, int y, bool primary) => new() { Text = text, Location = new Point(x, y), Size = new Size(100, 30), FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(57,97,170) : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(26,39,57), Cursor = Cursors.Hand };

    private void LoadCatalogs()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) { ShowDbHint(); return; }
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open();
            clientCombo.DataSource = ReadTable(connection, "SELECT ClienteId, NombreCompleto FROM Clientes WHERE Estado='Activo' AND DatosPurgados=FALSE ORDER BY NombreCompleto");
            clientCombo.DisplayMember = "NombreCompleto"; clientCombo.ValueMember = "ClienteId";
            planCombo.DataSource = ReadTable(connection, "SELECT PlanId, Nombre, DuracionDias, Precio FROM Planes WHERE Activo=TRUE ORDER BY Nombre");
            planCombo.DisplayMember = "Nombre"; planCombo.ValueMember = "PlanId";
            UpdatePreview();
            status.Text = clientCombo.Items.Count == 0 ? "Registra un cliente activo para comenzar." : planCombo.Items.Count == 0 ? "Registra un plan activo para comenzar." : "";
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { ShowDbError("No se pudieron cargar los clientes y planes.", ex); }
    }

    private static DataTable ReadTable(MySqlConnection connection, string sql)
    {
        using var command = new MySqlCommand(sql, connection); using var reader = command.ExecuteReader(); var table = new DataTable(); table.Load(reader); return table;
    }

    private void UpdatePreview()
    {
        if (planCombo.SelectedItem is not DataRowView plan) { preview.Text = "Selecciona un plan activo."; return; }
        int days = Convert.ToInt32(plan["DuracionDias"]); DateTime end = startDate.Value.Date.AddDays(days - 1);
        preview.Text = $"Duración: {days} días     Vence: {end:dd/MM/yyyy}     El precio queda guardado como importe acordado.";
    }

    private void LoadPlanPrice()
    {
        if (planCombo.SelectedItem is not DataRowView plan) return;
        decimal planPrice = Convert.ToDecimal(plan["Precio"]);
        agreedPrice.Value = Math.Min(planPrice, agreedPrice.Maximum);
    }
    private void LoadMemberships()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) { ShowDbHint(); return; }
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open();
            using (var update = new MySqlCommand("UPDATE Membresias SET Estado='Vencida' WHERE Estado='Activa' AND FechaVencimiento<CURDATE()", connection)) AuditLog.Execute(connection, update, "Actualizar membresías vencidas", "Membresias");
            const string sql = "SELECT m.MembresiaId, c.NombreCompleto AS Cliente, p.Nombre AS Plan, m.FechaInicio, m.FechaVencimiento, m.PrecioAcordado, m.Estado, m.EstadoPago FROM Membresias m INNER JOIN Clientes c ON c.ClienteId=m.ClienteId INNER JOIN Planes p ON p.PlanId=m.PlanId ORDER BY m.MembresiaId DESC";
            grid.DataSource = ReadTable(connection, sql);
            var idColumn = grid.Columns["MembresiaId"]; if (idColumn is not null) idColumn.Visible = false;
            if (grid.Columns["PrecioAcordado"] is DataGridViewColumn priceColumn) { priceColumn.HeaderText = "Precio (MXN)"; priceColumn.DefaultCellStyle.Format = "C2"; priceColumn.DefaultCellStyle.FormatProvider = CultureInfo.GetCultureInfo("es-MX"); }
            status.Text = $"{grid.Rows.Count} membresías";
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { ShowDbError("No se pudieron cargar las membresías.", ex); }
    }

    private void SaveMembership()
    {
        if (clientCombo.SelectedValue is null || planCombo.SelectedItem is not DataRowView plan) { MessageBox.Show("Necesitas un cliente y un plan activos para asignar una membresía."); return; }
        try
        {
            GymOperations.AssignMembership(Convert.ToInt32(clientCombo.SelectedValue), Convert.ToInt32(planCombo.SelectedValue), startDate.Value, agreedPrice.Value);
            ClearForm(); LoadMemberships(); MessageBox.Show("Membresía asignada y guardada.", "Integra Gym", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is MySqlException or InvalidOperationException or UnauthorizedAccessException) { MessageBox.Show(this, ex.Message, "No se pudo guardar la membresía", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void SelectMembership()
    {
        if (grid.CurrentRow?.DataBoundItem is not DataRowView row) { selectedMembershipId = null; cancelButton.Enabled = false; return; }
        selectedMembershipId = Convert.ToInt32(row["MembresiaId"]); selectedState = Convert.ToString(row["Estado"]) ?? ""; cancelButton.Enabled = selectedState != "Cancelada";
    }

    private void CancelMembership()
    {
        if (selectedMembershipId is not int id) { MessageBox.Show("Selecciona una membresía activa o vencida."); return; }
        if (MessageBox.Show("¿Cancelar la membresía seleccionada? El historial y los pagos se conservarán.", "Confirmar cancelación", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { using var connection = new MySqlConnection(ConnectionString); connection.Open(); using var command = new MySqlCommand("UPDATE Membresias SET Estado='Cancelada' WHERE MembresiaId=@id AND Estado<>'Cancelada'", connection); command.Parameters.AddWithValue("@id", id); AuditLog.Execute(connection, command, "Cancelar membresía", "Membresias", id); selectedMembershipId = null; LoadMemberships(); }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { ShowDbError("No se pudo cancelar la membresía.", ex); }
    }

    private void ClearForm() { startDate.Value = DateTime.Today; LoadPlanPrice(); selectedMembershipId = null; grid.ClearSelection(); UpdatePreview(); }
    private static void ShowDbHint() => MessageBox.Show("Configura MySQL local y registra clientes y planes activos antes de asignar membresías.", "Base de datos", MessageBoxButtons.OK, MessageBoxIcon.Information);
    private static void ShowDbError(string message, Exception ex) => MessageBox.Show($"{message}\n\n{ex.Message}", "Error de base de datos", MessageBoxButtons.OK, MessageBoxIcon.Error);
}




