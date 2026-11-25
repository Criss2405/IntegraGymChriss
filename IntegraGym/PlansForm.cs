using MySqlConnector;
using System.Data;

namespace IntegraGym;

internal sealed class PlansForm : StudioForm
{
    private static string ConnectionString => DatabaseSettings.ConnectionString;
    private readonly DataGridView grid = new();
    private readonly TextBox nameInput = new();
    private readonly NumericUpDown daysInput = new();
    private readonly NumericUpDown priceInput = new();
    private readonly CheckBox activeInput = new();
    private readonly Label status = new();
    private int? selectedId;

    public PlansForm()
    {
        Text = "Integra Gym | Planes"; MinimumSize = new Size(900, 580); StartPosition = FormStartPosition.CenterParent; WindowState = FormWindowState.Maximized; BackColor = Color.FromArgb(244,247,251); BuildLayout(); AppUi.StyleForm(this); Load += (_, _) => LoadPlans();
    }

    private void BuildLayout()
    {
        Controls.Add(new Label { Text = "Planes", Font = new Font("Segoe UI", 22, FontStyle.Bold), AutoSize = true, Location = new Point(28, 20) });
        Controls.Add(new Label { Text = "Administra el catálogo de planes y sus precios.", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(31, 61) });
        var editor = new Panel { Location = new Point(28, 100), Size = new Size(830, 130), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.White };
        AddField(editor, "Nombre del plan", nameInput, 15, 12, 235);
        daysInput.Location = new Point(270, 36); daysInput.Width = 105; daysInput.Minimum = 1; daysInput.Maximum = 3650; daysInput.Value = 30; editor.Controls.Add(new Label { Text = "Duración (días)", Location = new Point(270, 12), AutoSize = true }); editor.Controls.Add(daysInput);
        priceInput.Location = new Point(395, 36); priceInput.Width = 125; priceInput.Minimum = 0; priceInput.Maximum = 1000000; priceInput.DecimalPlaces = 2; priceInput.ThousandsSeparator = true; editor.Controls.Add(new Label { Text = "Precio (MXN)", Location = new Point(395, 12), AutoSize = true }); editor.Controls.Add(priceInput);
        activeInput.Text = "Activo"; activeInput.Checked = true; activeInput.Location = new Point(540, 39); activeInput.AutoSize = true; editor.Controls.Add(activeInput);
        var save = MakeButton("Guardar", 700, 28, true); save.Click += (_, _) => SavePlan(); editor.Controls.Add(save);
        var clear = MakeButton("Limpiar", 700, 68, false); clear.Click += (_, _) => ClearForm(); editor.Controls.Add(clear); Controls.Add(editor);
        grid.Location = new Point(28, 248); grid.Size = new Size(830, 250); grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; grid.ReadOnly = true; AppUi.ConfigureGrid(grid); grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.AllowUserToAddRows = false; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None; grid.SelectionChanged += (_, _) => SelectPlan(); Controls.Add(grid);
        var refresh = MakeButton("Actualizar lista", 28, 514, false); refresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; refresh.Click += (_, _) => LoadPlans(); Controls.Add(refresh);
        var deactivate = MakeButton("Desactivar", 140, 514, false); deactivate.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; deactivate.Click += (_, _) => DeactivatePlan(); Controls.Add(deactivate);
        status.Location = new Point(250, 521); status.AutoSize = true; status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; status.ForeColor = Color.DimGray; Controls.Add(status);
    }

    private static void AddField(Control parent, string caption, TextBox input, int x, int y, int width)
    {
        parent.Controls.Add(new Label { Text = caption, Location = new Point(x, y), AutoSize = true, ForeColor = Color.FromArgb(102,117,136) }); input.Location = new Point(x, y + 24); input.Width = width; parent.Controls.Add(input);
    }

    private static Button MakeButton(string text, int x, int y, bool primary) => new() { Text = text, Location = new Point(x, y), Size = new Size(100, 30), FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(57,97,170) : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(26,39,57), Cursor = Cursors.Hand };

    private void LoadPlans()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) { MessageBox.Show("Configura INTEGRAGYM_DB_CONNECTION."); return; }
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open(); using var command = new MySqlCommand("SELECT PlanId, Nombre, DuracionDias, Precio, Activo FROM Planes ORDER BY Nombre", connection); using var reader = command.ExecuteReader(); var table = new DataTable(); table.Load(reader); grid.DataSource = table; var idColumn = grid.Columns["PlanId"]; if (idColumn is not null) idColumn.Visible = false; status.Text = $"{table.Rows.Count} planes";
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { status.Text = "No se pudo conectar con MySQL."; MessageBox.Show($"No se pudieron cargar los planes.\n\n{ex.Message}", "Error de base de datos", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SavePlan()
    {
        var name = nameInput.Text.Trim(); if (name.Length < 2) { MessageBox.Show("Escribe el nombre del plan."); nameInput.Focus(); return; }
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open(); using var command = connection.CreateCommand();
            if (selectedId is int id) { command.CommandText = "UPDATE Planes SET Nombre=@nombre, DuracionDias=@dias, Precio=@precio, Activo=@activo WHERE PlanId=@id"; command.Parameters.AddWithValue("@id", id); }
            else command.CommandText = "INSERT INTO Planes(Nombre,DuracionDias,Precio,Activo) VALUES(@nombre,@dias,@precio,@activo)";
            command.Parameters.AddWithValue("@nombre", name); command.Parameters.AddWithValue("@dias", (int)daysInput.Value); command.Parameters.AddWithValue("@precio", priceInput.Value); command.Parameters.AddWithValue("@activo", activeInput.Checked); AuditLog.Execute(connection, command, selectedId is null ? "Registrar plan" : "Modificar plan", "Planes", selectedId); ClearForm(); LoadPlans();
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { MessageBox.Show($"No se pudo guardar el plan. El nombre debe ser único.\n\n{ex.Message}", "Error al guardar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void DeactivatePlan()
    {
        if (selectedId is not int id) { MessageBox.Show("Selecciona un plan."); return; }
        if (MessageBox.Show("¿Desactivar el plan? Se conservará para el historial de membresías.", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { using var connection = new MySqlConnection(ConnectionString); connection.Open(); using var command = new MySqlCommand("UPDATE Planes SET Activo=FALSE WHERE PlanId=@id AND Activo=TRUE", connection); command.Parameters.AddWithValue("@id", id); AuditLog.Execute(connection, command, "Desactivar plan", "Planes", id); ClearForm(); LoadPlans(); }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { MessageBox.Show($"No se pudo desactivar el plan.\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SelectPlan()
    {
        if (grid.CurrentRow?.DataBoundItem is not DataRowView row) return; selectedId = Convert.ToInt32(row["PlanId"]); nameInput.Text = Convert.ToString(row["Nombre"]) ?? ""; daysInput.Value = Convert.ToDecimal(row["DuracionDias"]); priceInput.Value = Convert.ToDecimal(row["Precio"]); activeInput.Checked = Convert.ToBoolean(row["Activo"]);
    }

    private void ClearForm() { selectedId = null; nameInput.Clear(); daysInput.Value = 30; priceInput.Value = 0; activeInput.Checked = true; grid.ClearSelection(); }
}




