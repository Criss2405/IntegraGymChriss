using MySqlConnector;
using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;

namespace IntegraGym;

internal sealed class UsersForm : StudioForm
{
    private static string ConnectionString => DatabaseSettings.ConnectionString;
    private readonly TabControl tabs = new() { Location = new Point(24, 92), Size = new Size(1010, 535), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
    private readonly DataGridView usersGrid = new();
    private readonly DataGridView permissionsGrid = new();
    private readonly TextBox nameInput = new();
    private readonly TextBox emailInput = new();
    private readonly TextBox passwordInput = new() { UseSystemPasswordChar = true };
    private readonly ComboBox roleInput = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox permissionsRole = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox activeInput = new() { Text = "Cuenta activa", Checked = true, AutoSize = true };
    private readonly Label userStatus = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Label permissionStatus = new() { AutoSize = true, ForeColor = Color.DimGray };
    private int? selectedUserId;
    private bool loadingRoles;

    public UsersForm()
    {
        AppSession.Require("usuarios.gestionar"); SecurityService.RequireAdmin();
        Text = "Integra Gym | Usuarios y permisos"; MinimumSize = new Size(1080, 700);
        StartPosition = FormStartPosition.CenterParent; WindowState = FormWindowState.Maximized; BackColor = Color.FromArgb(244,247,251);
        Controls.Add(new Label { Text = "Usuarios y permisos", Font = new Font("Segoe UI", 22, FontStyle.Bold), AutoSize = true, Location = new Point(28, 18) });
        Controls.Add(new Label { Text = "Administra las cuentas de acceso y las funciones disponibles por rol.", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(31, 58) });
        var users = new TabPage("Cuentas de usuario") { BackColor = BackColor };
        var permissions = new TabPage("Permisos por rol") { BackColor = BackColor };
        BuildUsersTab(users); BuildPermissionsTab(permissions); tabs.TabPages.Add(users); tabs.TabPages.Add(permissions); Controls.Add(tabs);
        AppUi.StyleForm(this);
        Load += (_, _) => LoadData();
    }

    private void BuildUsersTab(TabPage page)
    {
        var editor = new Panel { Location = new Point(8, 8), Size = new Size(970, 160), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.White };
        AddField(editor, "Nombre", nameInput, 14, 12, 205); AddField(editor, "Correo de acceso", emailInput, 235, 12, 235);
        AddField(editor, "Contraseña (mínimo 12 caracteres)", passwordInput, 490, 12, 250);
        AddLabel(editor, "Rol", 14, 82); roleInput.Location = new Point(14, 108); roleInput.Width = 205; editor.Controls.Add(roleInput);
        activeInput.Location = new Point(235, 112); editor.Controls.Add(activeInput);
        var save = MakeButton("Guardar usuario", 490, 103, true); save.Width = 125; save.Click += (_, _) => RunSafely(SaveUser); editor.Controls.Add(save);
        var clear = MakeButton("Limpiar", 625, 103, false); clear.Click += (_, _) => ClearUserForm(); editor.Controls.Add(clear); page.Controls.Add(editor);
        ConfigureGrid(usersGrid); usersGrid.Location = new Point(8, 182); usersGrid.Size = new Size(970, 260); usersGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        usersGrid.SelectionChanged += (_, _) => SelectUser(); page.Controls.Add(usersGrid);
        var refresh = MakeButton("Actualizar lista", 8, 455, false); refresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; refresh.Click += (_, _) => LoadUsers(); page.Controls.Add(refresh);
        var toggle = MakeButton("Activar / desactivar", 125, 455, false); toggle.Width = 155; toggle.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; toggle.Click += (_, _) => RunSafely(ToggleUserStatus); page.Controls.Add(toggle);
        userStatus.Location = new Point(298, 463); userStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; page.Controls.Add(userStatus);
    }

    private void BuildPermissionsTab(TabPage page)
    {
        AddLabel(page, "Rol que vas a configurar:", 12, 15);
        permissionsRole.Location = new Point(12, 42); permissionsRole.Width = 270; page.Controls.Add(permissionsRole);
        page.Controls.Add(new Label { Text = "Cada fila es un permiso. Marca o desmarca la casilla y guarda los cambios.", Location = new Point(12, 74), AutoSize = true, ForeColor = Color.DimGray });
        page.Controls.Add(new Label { Text = "Iniciales: Administrador (todos); Recepción (Clientes, Membresías, Pagos y Reportes); Entrenador (Clientes y Membresías).", Location = new Point(12, 92), MaximumSize = new Size(950, 0), AutoSize = true, ForeColor = Color.DimGray });
        ConfigureGrid(permissionsGrid); permissionsGrid.Location = new Point(12, 130); permissionsGrid.Size = new Size(960, 292); permissionsGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        permissionsGrid.ReadOnly = false; permissionsGrid.AllowUserToAddRows = false; permissionsGrid.CellContentClick += (_, e) => { if (e.RowIndex >= 0) permissionsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); }; page.Controls.Add(permissionsGrid);
        var save = MakeButton("Guardar permisos", 12, 438, true); save.Width = 140; save.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; save.Click += (_, _) => RunSafely(SavePermissions); page.Controls.Add(save);
        permissionStatus.Location = new Point(166, 446); permissionStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; page.Controls.Add(permissionStatus);
        permissionsRole.SelectedIndexChanged += (_, _) => { if (!loadingRoles) LoadPermissions(); };
    }

    private static void AddLabel(Control parent, string text, int x, int y) =>
        parent.Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = Color.FromArgb(102,117,136) });
    private static void AddField(Control parent, string caption, TextBox input, int x, int y, int width)
    {
        AddLabel(parent, caption, x, y); input.Location = new Point(x, y + 25); input.Width = width; parent.Controls.Add(input);
    }
    private static Button MakeButton(string text, int x, int y, bool primary) => new()
    {
        Text = text, Location = new Point(x, y), Size = new Size(105, 32), FlatStyle = FlatStyle.Flat,
        BackColor = primary ? Color.FromArgb(57,97,170) : Color.White,
        ForeColor = primary ? Color.White : Color.FromArgb(26,39,57), Cursor = Cursors.Hand
    };
    private static void ConfigureGrid(DataGridView grid)
    {
        grid.ReadOnly = true; grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None; grid.AllowUserToAddRows = false; grid.RowHeadersVisible = false; AppUi.ConfigureGrid(grid);
    }

    private void LoadData()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            MessageBox.Show("Configura la conexión local de MySQL antes de administrar usuarios.", "Base de datos", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open();
            AccessControl.Initialize(connection);
            LoadRoles(connection); LoadUsers(connection);
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { ShowDbError("No se pudo preparar la administración de usuarios.", ex); }
    }

    private void LoadRoles(MySqlConnection? existing = null)
    {
        loadingRoles = true;
        try
        {
            DataTable roles;
            if (existing is not null) roles = ReadTable(existing, "SELECT RolId,Nombre FROM Roles WHERE Activo=TRUE ORDER BY Nombre");
            else { using var c = new MySqlConnection(ConnectionString); c.Open(); roles = ReadTable(c, "SELECT RolId,Nombre FROM Roles WHERE Activo=TRUE ORDER BY Nombre"); }
            roleInput.DataSource = roles.Copy(); roleInput.DisplayMember = "Nombre"; roleInput.ValueMember = "RolId";
            permissionsRole.DataSource = roles; permissionsRole.DisplayMember = "Nombre"; permissionsRole.ValueMember = "RolId";
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { ShowDbError("No se pudieron cargar los roles.", ex); }
        finally { loadingRoles = false; }
        LoadPermissions();
    }

    private void LoadUsers(MySqlConnection? existing = null)
    {
        try
        {
            const string sql = "SELECT u.UsuarioId,u.Nombre,u.Correo,r.Nombre AS Rol,u.RolId,u.Activo,u.FechaAlta FROM Usuarios u INNER JOIN Roles r ON r.RolId=u.RolId ORDER BY u.Nombre";
            DataTable users;
            if (existing is not null) users = ReadTable(existing, sql);
            else { using var c = new MySqlConnection(ConnectionString); c.Open(); users = ReadTable(c, sql); }
            usersGrid.DataSource = users;
            if (usersGrid.Columns["UsuarioId"] is DataGridViewColumn id) id.Visible = false;
            if (usersGrid.Columns["RolId"] is DataGridViewColumn roleId) roleId.Visible = false;
            userStatus.Text = users.Rows.Count.ToString() + " cuentas";
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { ShowDbError("No se pudo cargar la lista de usuarios.", ex); }
    }

    private static DataTable ReadTable(MySqlConnection connection, string sql)
    {
        using var command = new MySqlCommand(sql, connection); using var reader = command.ExecuteReader();
        var table = new DataTable(); table.Load(reader); return table;
    }

    private void SelectUser()
    {
        if (usersGrid.CurrentRow?.DataBoundItem is not DataRowView row) { selectedUserId = null; return; }
        selectedUserId = Convert.ToInt32(row["UsuarioId"]); nameInput.Text = Convert.ToString(row["Nombre"]) ?? "";
        emailInput.Text = Convert.ToString(row["Correo"]) ?? ""; passwordInput.Clear(); activeInput.Checked = Convert.ToBoolean(row["Activo"]);
        if (roleInput.DataSource is not null) roleInput.SelectedValue = Convert.ToInt32(row["RolId"]);
    }

    private void SaveUser()
    {
        string name = nameInput.Text.Trim(), email = emailInput.Text.Trim(), password = passwordInput.Text;
        if (name.Length < 2) { MessageBox.Show("Escribe el nombre del usuario."); nameInput.Focus(); return; }
        if (!MailAddress.TryCreate(email, out _)) { MessageBox.Show("Escribe un correo válido para el acceso."); emailInput.Focus(); return; }
        if (roleInput.SelectedValue is null) { MessageBox.Show("Selecciona un rol activo."); return; }
        if ((selectedUserId is null || password.Length > 0) && password.Length < 12) { MessageBox.Show("La contraseña debe tener al menos 12 caracteres."); passwordInput.Focus(); return; }
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open(); using var command = connection.CreateCommand();
            command.Parameters.AddWithValue("@nombre", name); command.Parameters.AddWithValue("@correo", email);
            command.Parameters.AddWithValue("@rol", Convert.ToInt32(roleInput.SelectedValue)); command.Parameters.AddWithValue("@activo", activeInput.Checked);
            if (selectedUserId is int id)
            {
                command.CommandText = password.Length == 0
                    ? "UPDATE Usuarios SET Nombre=@nombre,Correo=@correo,RolId=@rol,Activo=@activo WHERE UsuarioId=@id"
                    : "UPDATE Usuarios SET Nombre=@nombre,Correo=@correo,RolId=@rol,Activo=@activo,HashContrasena=@hash WHERE UsuarioId=@id";
                command.Parameters.AddWithValue("@id", id); if (password.Length > 0) command.Parameters.AddWithValue("@hash", PasswordHasher.Hash(password));
            }
            else
            {
                command.CommandText = "INSERT INTO Usuarios(RolId,Nombre,Correo,HashContrasena,Activo) VALUES(@rol,@nombre,@correo,@hash,@activo)";
                command.Parameters.AddWithValue("@hash", PasswordHasher.Hash(password));
            }
            GuardUserChange(connection, selectedUserId, Convert.ToInt32(roleInput.SelectedValue), activeInput.Checked);
            using var transaction = connection.BeginTransaction(); command.Transaction = transaction; command.ExecuteNonQuery(); int savedId = selectedUserId ?? checked((int)command.LastInsertedId);
            if (password.Length > 0) SecurityService.SetPassword(connection, transaction, savedId, password, true);
            AuditLog.Write(connection, transaction, selectedUserId is null ? "Crear usuario" : password.Length > 0 ? "Modificar usuario y contraseña" : "Modificar usuario", "Usuarios", savedId); transaction.Commit(); ClearUserForm(); LoadUsers();
        }
        catch (InvalidOperationException ex) { MessageBox.Show(ex.Message); }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { ShowDbError("No se pudo guardar el usuario. Verifica que el correo no esté registrado.", ex); }
    }

    private static void GuardUserChange(MySqlConnection c, int? id, int role, bool active)
    {
        AppSession.Require("usuarios.gestionar"); SecurityService.RequireAdmin();
        if (id is null) return;
        using var q = new MySqlCommand("SELECT r.Nombre FROM Usuarios u JOIN Roles r ON r.RolId=u.RolId WHERE u.UsuarioId=@id",c); q.Parameters.AddWithValue("@id", id);
        if (Convert.ToString(q.ExecuteScalar()) == "Administrador" && (!active || role != AccessControl.GetAdministratorRoleId(c))) throw new InvalidOperationException("No se permite desactivar ni cambiar el rol de un administrador desde esta pantalla.");
    }
    private void ToggleUserStatus()
    {
        if (selectedUserId is not int id) { MessageBox.Show("Selecciona una cuenta."); return; }
        bool activate = !activeInput.Checked;
        using (var guard = SecurityService.Open()) GuardUserChange(guard, id, Convert.ToInt32(roleInput.SelectedValue), activate);
        string question = activate ? "¿Reactivar esta cuenta?" : "¿Desactivar esta cuenta?";
        if (MessageBox.Show(question, "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open();
            using var command = new MySqlCommand("UPDATE Usuarios SET Activo=@activo WHERE UsuarioId=@id", connection);
            command.Parameters.AddWithValue("@activo", activate); command.Parameters.AddWithValue("@id", id); AuditLog.Execute(connection, command, activate ? "Activar usuario" : "Desactivar usuario", "Usuarios", id);
            activeInput.Checked = activate; LoadUsers();
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { ShowDbError("No se pudo cambiar el estado de la cuenta.", ex); }
    }

    private void LoadPermissions()
    {
        if (permissionsRole.SelectedValue is null || string.IsNullOrWhiteSpace(ConnectionString)) return;
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open();
            using var command = new MySqlCommand("SELECT p.PermisoId,p.Codigo,p.Descripcion,(rp.PermisoId IS NOT NULL) AS Asignado FROM Permisos p LEFT JOIN RolesPermisos rp ON rp.PermisoId=p.PermisoId AND rp.RolId=@rol ORDER BY p.Codigo", connection);
            command.Parameters.AddWithValue("@rol", Convert.ToInt32(permissionsRole.SelectedValue));
            using var reader = command.ExecuteReader();
            var table = new DataTable();
            table.Columns.Add("PermisoId", typeof(int));
            table.Columns.Add("Asignado", typeof(bool));
            table.Columns.Add("Modulo", typeof(string));
            table.Columns.Add("Descripcion", typeof(string));
            while (reader.Read())
            {
                table.Rows.Add(
                    Convert.ToInt32(reader["PermisoId"]),
                    Convert.ToBoolean(reader["Asignado"]),
                    ModuleLabel(Convert.ToString(reader["Codigo"]) ?? ""),
                    Convert.ToString(reader["Descripcion"]) ?? "");
            }

            permissionsGrid.AutoGenerateColumns = false;
            permissionsGrid.Columns.Clear();
            permissionsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "PermisoId", DataPropertyName = "PermisoId", Visible = false
            });
            permissionsGrid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "Asignado", DataPropertyName = "Asignado", HeaderText = "Habilitado",
                ReadOnly = false, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 110
            });
            permissionsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Modulo", DataPropertyName = "Modulo", HeaderText = "Módulo",
                ReadOnly = true, FillWeight = 25
            });
            permissionsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Descripcion", DataPropertyName = "Descripcion", HeaderText = "Qué podrá hacer",
                ReadOnly = true, FillWeight = 75
            });
            permissionsGrid.DataSource = table;
            permissionStatus.Text = "Los cambios afectan a todo el rol y se aplican al volver a iniciar sesión.";
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { ShowDbError("No se pudieron cargar los permisos.", ex); }
    }
    private static string ModuleLabel(string code) => code switch
    {
        "clientes.gestionar" => "Clientes", "planes.gestionar" => "Planes",
        "membresias.gestionar" => "Membresías", "pagos.gestionar" => "Pagos",
        "entrenadores.gestionar" => "Entrenadores", "reportes.ver" => "Reportes",
        "usuarios.gestionar" => "Usuarios", "asistencias.gestionar" => "Asistencias",
        "bitacora.ver" => "Bitácora", "respaldos.crear" => "Copia de seguridad", _ => code
    };

    private void SavePermissions()
    {
        AppSession.Require("usuarios.gestionar"); SecurityService.RequireAdmin();
        if (permissionsRole.SelectedValue is null) { MessageBox.Show("Selecciona un rol."); return; }
        using (var c = SecurityService.Open())
            if (Convert.ToInt32(permissionsRole.SelectedValue) == AccessControl.GetAdministratorRoleId(c)) { MessageBox.Show("El rol Administrador conserva sus permisos completos. Configura Recepción o Entrenador."); return; }
        permissionsGrid.EndEdit();
        if (!permissionsGrid.Rows.Cast<DataGridViewRow>().Any(row => !row.IsNewRow && Convert.ToBoolean(row.Cells["Asignado"].Value)))
        {
            MessageBox.Show("Asigna al menos un permiso al rol para que conserve acceso al sistema.", "Falta un permiso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        int roleId = Convert.ToInt32(permissionsRole.SelectedValue);
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open(); using var transaction = connection.BeginTransaction();
            using (var delete = new MySqlCommand("DELETE FROM RolesPermisos WHERE RolId=@rol", connection, transaction))
            { delete.Parameters.AddWithValue("@rol", roleId); delete.ExecuteNonQuery(); }
            foreach (DataGridViewRow row in permissionsGrid.Rows)
            {
                if (row.IsNewRow || !Convert.ToBoolean(row.Cells["Asignado"].Value)) continue;
                using var insert = new MySqlCommand("INSERT INTO RolesPermisos(RolId,PermisoId) VALUES(@rol,@permiso)", connection, transaction);
                insert.Parameters.AddWithValue("@rol", roleId); insert.Parameters.AddWithValue("@permiso", Convert.ToInt32(row.Cells["PermisoId"].Value)); insert.ExecuteNonQuery();
            }
            AuditLog.Write(connection, transaction, "Modificar permisos del rol", "Roles", roleId);
            transaction.Commit(); permissionStatus.Text = "Permisos guardados. Se aplican al volver a iniciar sesión.";
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException) { ShowDbError("No se pudieron guardar los permisos. La cuenta necesita SELECT, INSERT y DELETE sobre integra_gym.*.", ex); }
    }

    private void RunSafely(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException or InvalidOperationException)
        { MessageBox.Show(this, ex.Message, "No se completó la operación", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void ClearUserForm()
    {
        selectedUserId = null; nameInput.Clear(); emailInput.Clear(); passwordInput.Clear(); activeInput.Checked = true; usersGrid.ClearSelection();
        if (roleInput.Items.Count > 0) roleInput.SelectedIndex = 0;
    }
    private static void ShowDbError(string message, Exception ex) =>
        MessageBox.Show(message + "\n\n" + ex.Message, "Error de base de datos", MessageBoxButtons.OK, MessageBoxIcon.Error);
}












