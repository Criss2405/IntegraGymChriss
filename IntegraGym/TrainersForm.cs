using MySqlConnector;
using System.Data;

namespace IntegraGym;

internal sealed class TrainersForm : StudioForm
{
    private static string ConnectionString => DatabaseSettings.ConnectionString;
    private readonly DataGridView grid = new();
    private readonly TextBox nameInput = new();
    private readonly TextBox emailInput = new();
    private readonly TextBox phoneInput = new();
    private readonly CheckBox showInactive = new() { Text = "Mostrar inactivos", AutoSize = true, Location = new Point(28, 216) };
    private readonly Button statusButton = new() { Text = "Dar de baja", Location = new Point(140, 514), Size = new Size(100, 30), FlatStyle = FlatStyle.Flat };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private int? selectedId;
    private string selectedStatus = "";

    public TrainersForm()
    {
        Text = "Integra Gym | Entrenadores"; MinimumSize = new Size(900, 580); StartPosition = FormStartPosition.CenterParent; WindowState = FormWindowState.Maximized; BackColor = Color.FromArgb(244,247,251);
        BuildLayout(); AppUi.StyleForm(this); Load += (_, _) => LoadTrainers();
    }

    private void BuildLayout()
    {
        Controls.Add(new Label { Text = "Entrenadores", Font = new Font("Segoe UI", 22, FontStyle.Bold), AutoSize = true, Location = new Point(28, 20) });
        Controls.Add(new Label { Text = "Administra los datos y el estado del equipo de entrenamiento.", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(31, 61) });
        var editor = new Panel { Location = new Point(28, 100), Size = new Size(830, 112), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.White };
        AddField(editor, "Nombre completo", nameInput, 15, 12, 240); AddField(editor, "Correo", emailInput, 275, 12, 240); AddField(editor, "Teléfono", phoneInput, 535, 12, 170);
        var save = MakeButton("Guardar", 715, 33, true); save.Click += (_, _) => SaveTrainer(); editor.Controls.Add(save);
        var clear = MakeButton("Limpiar", 715, 70, false); clear.Click += (_, _) => ClearForm(); editor.Controls.Add(clear); Controls.Add(editor);
        showInactive.CheckedChanged += (_, _) => { selectedId = null; selectedStatus = ""; LoadTrainers(); }; Controls.Add(showInactive);
        grid.Location = new Point(28, 242); grid.Size = new Size(830, 258); grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grid.ReadOnly = true; AppUi.ConfigureGrid(grid); grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.AllowUserToAddRows = false; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None; grid.SelectionChanged += (_, _) => SelectTrainer(); Controls.Add(grid);
        var refresh = MakeButton("Actualizar lista", 28, 514, false); refresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; refresh.Click += (_, _) => LoadTrainers(); Controls.Add(refresh);
        statusButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; statusButton.Click += (_, _) => ChangeStatus(); Controls.Add(statusButton);
        status.Location = new Point(250, 521); status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; Controls.Add(status);
    }

    private static void AddField(Control parent, string caption, TextBox input, int x, int y, int width)
    {
        parent.Controls.Add(new Label { Text = caption, Location = new Point(x, y), AutoSize = true, ForeColor = Color.FromArgb(102,117,136) }); input.Location = new Point(x, y + 24); input.Width = width; parent.Controls.Add(input);
    }

    private static Button MakeButton(string text, int x, int y, bool primary) => new() { Text = text, Location = new Point(x, y), Size = new Size(100, 30), FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(57,97,170) : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(26,39,57), Cursor = Cursors.Hand };

    private void LoadTrainers()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) { MessageBox.Show("Configura la conexión local de MySQL."); return; }
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open();
            using var command = new MySqlCommand("SELECT EntrenadorId, NombreCompleto, Correo, Telefono, Estado, FechaAlta FROM Entrenadores WHERE (@incluirInactivos=1 OR Estado='Activo') ORDER BY EntrenadorId DESC", connection);
            command.Parameters.AddWithValue("@incluirInactivos", showInactive.Checked); using var reader = command.ExecuteReader(); var table = new DataTable(); table.Load(reader); grid.DataSource = table;
            var idColumn = grid.Columns["EntrenadorId"]; if (idColumn is not null) idColumn.Visible = false;
            status.Text = $"{table.Rows.Count} entrenadores";
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { ShowDbError("No se pudo cargar el directorio de entrenadores.", ex); }
    }

    private void SaveTrainer()
    {
        string name = nameInput.Text.Trim(), email = emailInput.Text.Trim(), phone = phoneInput.Text.Trim();
        if (name.Length < 2) { MessageBox.Show("Escribe el nombre completo del entrenador."); nameInput.Focus(); return; }
        if (selectedId is not null && selectedStatus != "Activo") { MessageBox.Show("Este entrenador está inactivo. Reactívalo antes de editarlo."); return; }
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open(); using var command = connection.CreateCommand();
            if (selectedId is int id) { command.CommandText = "UPDATE Entrenadores SET NombreCompleto=@nombre, Correo=@correo, Telefono=@telefono WHERE EntrenadorId=@id AND Estado='Activo'"; command.Parameters.AddWithValue("@id", id); }
            else command.CommandText = "INSERT INTO Entrenadores(NombreCompleto,Correo,Telefono) VALUES(@nombre,@correo,@telefono)";
            command.Parameters.AddWithValue("@nombre", name); command.Parameters.AddWithValue("@correo", string.IsNullOrWhiteSpace(email) ? DBNull.Value : email); command.Parameters.AddWithValue("@telefono", string.IsNullOrWhiteSpace(phone) ? DBNull.Value : phone);
            AuditLog.Execute(connection, command, selectedId is null ? "Registrar entrenador" : "Modificar entrenador", "Entrenadores", selectedId); ClearForm(); LoadTrainers();
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { ShowDbError("No se pudo guardar el entrenador.", ex); }
    }

    private void SelectTrainer()
    {
        if (grid.CurrentRow?.DataBoundItem is not DataRowView row) return;
        selectedId = Convert.ToInt32(row["EntrenadorId"]); selectedStatus = Convert.ToString(row["Estado"]) ?? ""; statusButton.Text = selectedStatus == "Inactivo" ? "Reactivar" : "Dar de baja";
        nameInput.Text = Convert.ToString(row["NombreCompleto"]) ?? ""; emailInput.Text = row["Correo"] == DBNull.Value ? "" : Convert.ToString(row["Correo"]) ?? ""; phoneInput.Text = row["Telefono"] == DBNull.Value ? "" : Convert.ToString(row["Telefono"]) ?? "";
    }

    private void ChangeStatus()
    {
        if (selectedId is not int id) { MessageBox.Show("Selecciona un entrenador."); return; }
        bool reactivate = selectedStatus == "Inactivo"; string prompt = reactivate ? "¿Reactivar al entrenador?" : "¿Dar de baja al entrenador?";
        if (MessageBox.Show(prompt, "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { using var connection = new MySqlConnection(ConnectionString); connection.Open(); using var command = new MySqlCommand("UPDATE Entrenadores SET Estado=@estado WHERE EntrenadorId=@id", connection); command.Parameters.AddWithValue("@estado", reactivate ? "Activo" : "Inactivo"); command.Parameters.AddWithValue("@id", id); AuditLog.Execute(connection, command, reactivate ? "Reactivar entrenador" : "Dar de baja entrenador", "Entrenadores", id); ClearForm(); LoadTrainers(); }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { ShowDbError("No se pudo cambiar el estado del entrenador.", ex); }
    }

    private void ClearForm() { selectedId = null; selectedStatus = ""; statusButton.Text = "Dar de baja"; nameInput.Clear(); emailInput.Clear(); phoneInput.Clear(); grid.ClearSelection(); }
    private static void ShowDbError(string message, Exception ex) => MessageBox.Show($"{message}\n\n{ex.Message}", "Error de base de datos", MessageBoxButtons.OK, MessageBoxIcon.Error);
}



