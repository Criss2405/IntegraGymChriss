using MySqlConnector;
using System.Data;

namespace IntegraGym;

internal sealed class ClientsForm : StudioForm
{
    private static string ConnectionString => DatabaseSettings.ConnectionString;
    private readonly DataGridView grid = new();
    private readonly TextBox nameInput = new();
    private readonly TextBox emailInput = new();
    private readonly TextBox phoneInput = new();
    private readonly Label status = new();
    private readonly CheckBox showInactive = new() { Text = "Mostrar inactivos", AutoSize = true, Location = new Point(28, 216) };
    private readonly Button statusButton = new() { Text = "Dar de baja", Location = new Point(140, 514), Size = new Size(100, 30), FlatStyle = FlatStyle.Flat };
    private int? selectedId;
    private string selectedStatus = "";
    private bool selectedPurged;

    public ClientsForm()
    {
        Text = "Integra Gym | Clientes";
        MinimumSize = new Size(900, 580);
        StartPosition = FormStartPosition.CenterScreen; WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(244,247,251);
        BuildLayout(); AppUi.StyleForm(this);
        Load += (_, _) => LoadClients();
    }

    private void BuildLayout()
    {
        Controls.Add(new Label { Text = "Clientes", Font = new Font("Segoe UI", 22, FontStyle.Bold), AutoSize = true, Location = new Point(28, 20) });
        Controls.Add(new Label { Text = "Registra y administra el directorio del gimnasio.", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(31, 61) });
        var editor = new Panel { Location = new Point(28, 100), Size = new Size(830, 112), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.White };
        AddField(editor, "Nombre completo", nameInput, 15, 12, 240);
        AddField(editor, "Correo", emailInput, 275, 12, 240);
        AddField(editor, "Teléfono", phoneInput, 535, 12, 170);
        var save = MakeButton("Guardar", 715, 33, true); save.Click += (_, _) => SaveClient(); editor.Controls.Add(save);
        var clear = MakeButton("Limpiar", 715, 70, false); clear.Click += (_, _) => ClearForm(); editor.Controls.Add(clear);
        Controls.Add(editor);

        showInactive.CheckedChanged += (_, _) => { selectedId = null; selectedStatus = ""; LoadClients(); };
        Controls.Add(showInactive);
        grid.Location = new Point(28, 242); grid.Size = new Size(830, 258); grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grid.ReadOnly = true; AppUi.ConfigureGrid(grid); grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.AllowUserToAddRows = false;
        grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None; grid.SelectionChanged += (_, _) => SelectClient(); Controls.Add(grid);
        var refresh = MakeButton("Actualizar lista", 28, 514, false); refresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; refresh.Click += (_, _) => LoadClients(); Controls.Add(refresh);
        statusButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; statusButton.Click += (_, _) => ChangeClientStatus(); Controls.Add(statusButton);
        status.Location = new Point(250, 521); status.AutoSize = true; status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; status.ForeColor = Color.DimGray; Controls.Add(status);
    }

    private static void AddField(Control parent, string caption, TextBox input, int x, int y, int width)
    {
        parent.Controls.Add(new Label { Text = caption, Location = new Point(x, y), AutoSize = true, ForeColor = Color.FromArgb(102,117,136) });
        input.Location = new Point(x, y + 24); input.Width = width; input.Height = 28; parent.Controls.Add(input);
    }

    private static Button MakeButton(string text, int x, int y, bool primary) => new()
    {
        Text = text, Location = new Point(x, y), Size = new Size(100, 30), FlatStyle = FlatStyle.Flat,
        BackColor = primary ? Color.FromArgb(57,97,170) : Color.White,
        ForeColor = primary ? Color.White : Color.FromArgb(26,39,57), Cursor = Cursors.Hand
    };

    private void LoadClients()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) { ShowDatabaseHint(); return; }
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open();
            using var command = new MySqlCommand("SELECT ClienteId, NombreCompleto, Correo, Telefono, Estado, FechaRegistro, DatosPurgados FROM Clientes WHERE (@incluirInactivos=1 OR Estado='Activo') ORDER BY ClienteId DESC", connection);
            command.Parameters.AddWithValue("@incluirInactivos", showInactive.Checked);
            using var reader = command.ExecuteReader(); var table = new DataTable(); table.Load(reader); grid.DataSource = table;
            var idColumn = grid.Columns["ClienteId"]; if (idColumn is not null) idColumn.Visible = false;
            status.Text = $"{table.Rows.Count} clientes";
        }
        catch (MySqlException) { status.Text = "No se pudo conectar con MySQL."; MessageBox.Show("No se pudo conectar a MySQL local. Confirma que MySQL Server esté iniciado y revisa la conexión desde la pantalla de configuración de MySQL del inicio de sesión.", "Base de datos no disponible", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    private static void ShowDatabaseHint() => MessageBox.Show("Falta configurar la conexión local. Usa el botón de configuración de MySQL en el inicio de sesión.", "Configurar MySQL", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void SaveClient()
    {
        var name = nameInput.Text.Trim(); var email = emailInput.Text.Trim(); var phone = phoneInput.Text.Trim();
        if (name.Length < 2) { MessageBox.Show("Escribe el nombre completo del cliente."); nameInput.Focus(); return; }
        if (selectedPurged) { MessageBox.Show("Los datos personales de este cliente ya fueron depurados y no se pueden editar."); return; }
        if (selectedId is not null && selectedStatus != "Activo") { MessageBox.Show("Este cliente está inactivo. Reactívalo antes de editarlo."); return; }
        if (string.IsNullOrWhiteSpace(ConnectionString)) { ShowDatabaseHint(); return; }
        try
        {
            GymOperations.SaveClient(selectedId, name, email, phone); ClearForm(); LoadClients();
        }
        catch (Exception ex) when (ex is MySqlException or InvalidOperationException or UnauthorizedAccessException) { MessageBox.Show($"No se guardó el cliente.\n\n{ex.Message}", "Error al guardar", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void ChangeClientStatus()
    {
        if (selectedId is not int id) { MessageBox.Show("Selecciona un cliente de la lista."); return; }
        if (selectedPurged) { MessageBox.Show("Los datos personales de este cliente ya fueron depurados."); return; }
        bool reactivate = selectedStatus == "Inactivo";
        string question = reactivate ? "¿Reactivar al cliente seleccionado?" : "¿Dar de baja al cliente seleccionado? Se conservará su historial.";
        if (MessageBox.Show(question, "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open();
            using var command = new MySqlCommand("UPDATE Clientes SET Estado=@estado, FechaInactivacion=IF(@estado='Inactivo',CURRENT_TIMESTAMP,NULL), FechaActualizacion=CURRENT_TIMESTAMP WHERE ClienteId=@id AND DatosPurgados=FALSE", connection);
            command.Parameters.AddWithValue("@estado", reactivate ? "Activo" : "Inactivo"); command.Parameters.AddWithValue("@id", id); AuditLog.Execute(connection, command, reactivate ? "Reactivar cliente" : "Dar de baja cliente", "Clientes", id);
            ClearForm(); LoadClients();
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { MessageBox.Show($"No se pudo actualizar el estado. {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SelectClient()
    {
        if (grid.CurrentRow?.DataBoundItem is not DataRowView row) return;
        selectedId = Convert.ToInt32(row["ClienteId"]); selectedStatus = Convert.ToString(row["Estado"]) ?? ""; selectedPurged = Convert.ToBoolean(row["DatosPurgados"]);
        statusButton.Text = selectedPurged ? "Depurado" : selectedStatus == "Inactivo" ? "Reactivar" : "Dar de baja";
        statusButton.Enabled = !selectedPurged;
        nameInput.Text = Convert.ToString(row["NombreCompleto"]) ?? "";
        emailInput.Text = row["Correo"] == DBNull.Value ? "" : Convert.ToString(row["Correo"]) ?? "";
        phoneInput.Text = row["Telefono"] == DBNull.Value ? "" : Convert.ToString(row["Telefono"]) ?? "";
    }

    private void ClearForm()
    {
        selectedId = null; selectedStatus = ""; selectedPurged = false; statusButton.Enabled = true; statusButton.Text = "Dar de baja"; nameInput.Clear(); emailInput.Clear(); phoneInput.Clear(); grid.ClearSelection();
    }
}






