using MySqlConnector;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;

namespace IntegraGym;

internal sealed record AppUser(int Id, string Name, string Email, int RoleId, string RoleName, HashSet<string> Permissions)
{
    public bool Can(string permission) => Permissions.Contains(permission);
}

internal static class DatabaseSettings
{
    private const string EnvironmentVariableName = "INTEGRAGYM_DB_CONNECTION";
    private const string EncryptedConfigFileName = "dbconnection.bin";

    public static string ConnectionString
    {
        get
        {
            try { var saved = LocalSecrets.Read("connection.bin"); if (!string.IsNullOrWhiteSpace(saved)) return saved; } catch (Exception ex) when(ex is CryptographicException or IOException or UnauthorizedAccessException) { return ""; }
            var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariableName);
            if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;

            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, EncryptedConfigFileName);
                if (!File.Exists(path)) return "";
                return Encoding.UTF8.GetString(WindowsDataProtection.Unprotect(File.ReadAllBytes(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
            {
                return "";
            }
        }
    }

    private static class WindowsDataProtection
    {
        private const int UiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static byte[] Unprotect(byte[] encrypted)
        {
            var inputBuffer = Marshal.AllocHGlobal(encrypted.Length);
            var input = new DataBlob { Size = encrypted.Length, Data = inputBuffer };
            try
            {
                Marshal.Copy(encrypted, 0, inputBuffer, encrypted.Length);
                if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out var output))
                    throw new CryptographicException(Marshal.GetLastWin32Error());

                try
                {
                    var result = new byte[output.Size];
                    Marshal.Copy(output.Data, result, 0, output.Size);
                    return result;
                }
                finally
                {
                    if (output.Data != IntPtr.Zero) LocalFree(output.Data);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(inputBuffer);
            }
        }
    }
}

internal static class AccessControl
{
    private const int PasswordIterations = 310_000;
    private static readonly (string Code, string Description)[] PermissionCatalog =
    [
        ("clientes.gestionar", "Consultar el directorio, registrar clientes, actualizar datos y dar de baja"),
        ("planes.gestionar", "Consultar el catálogo, crear planes, editar datos y desactivarlos"),
        ("membresias.gestionar", "Asignar, consultar, renovar y cancelar membresías"),
        ("pagos.gestionar", "Registrar cobros y consultar el historial de pagos"),
        ("entrenadores.gestionar", "Registrar, editar y activar o dar de baja entrenadores"),
        ("reportes.ver", "Consultar y exportar reportes"),
        ("usuarios.gestionar", "Crear y editar cuentas, asignar roles y configurar permisos"),
        ("asistencias.gestionar", "Consultar y registrar entradas de clientes con membresía vigente"),
        ("bitacora.ver", "Consultar las acciones realizadas por los usuarios del sistema"),
        ("respaldos.crear", "Guardar una copia de seguridad completa de la base de datos")
    ];

    public static void Initialize(MySqlConnection connection)
    {
        var newCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in PermissionCatalog)
        {
            using var exists = new MySqlCommand("SELECT COUNT(*) FROM Permisos WHERE Codigo=@codigo", connection);
            exists.Parameters.AddWithValue("@codigo", item.Code);
            if (Convert.ToInt32(exists.ExecuteScalar()) == 0) newCodes.Add(item.Code);
            using var command = new MySqlCommand(
                "INSERT INTO Permisos(Codigo,Descripcion) VALUES(@codigo,@descripcion) ON DUPLICATE KEY UPDATE Descripcion=@descripcion",
                connection);
            command.Parameters.AddWithValue("@codigo", item.Code);
            command.Parameters.AddWithValue("@descripcion", item.Description);
            command.ExecuteNonQuery();
        }

        var defaults = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Administrador"] = PermissionCatalog.Select(item => item.Code).ToArray(),
            ["Recepción"] = ["clientes.gestionar", "membresias.gestionar", "pagos.gestionar", "reportes.ver", "asistencias.gestionar"],
            ["Entrenador"] = ["clientes.gestionar", "membresias.gestionar", "asistencias.gestionar"]
        };
        foreach (var role in defaults)
        {
            using var findRole = new MySqlCommand("SELECT RolId FROM Roles WHERE Nombre=@nombre AND Activo=TRUE", connection);
            findRole.Parameters.AddWithValue("@nombre", role.Key);
            object? value = findRole.ExecuteScalar();
            if (value is null || value == DBNull.Value) continue;
            int roleId = Convert.ToInt32(value);

            using var count = new MySqlCommand("SELECT COUNT(*) FROM RolesPermisos WHERE RolId=@rol", connection);
            count.Parameters.AddWithValue("@rol", roleId);
            bool hasPermissions = Convert.ToInt32(count.ExecuteScalar()) != 0;

            foreach (string code in role.Value)
            {
                if (!newCodes.Contains(code)) continue;
                using var insert = new MySqlCommand(
                    "INSERT IGNORE INTO RolesPermisos(RolId,PermisoId) SELECT @rol,PermisoId FROM Permisos WHERE Codigo=@codigo",
                    connection);
                insert.Parameters.AddWithValue("@rol", roleId);
                insert.Parameters.AddWithValue("@codigo", code);
                insert.ExecuteNonQuery();
            }
        }
    }

    public static int GetUserCount(MySqlConnection connection)
    {
        using var command = new MySqlCommand("SELECT COUNT(*) FROM Usuarios", connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public static int GetAdministratorCount(MySqlConnection connection)
    {
        using var command = new MySqlCommand("SELECT COUNT(*) FROM Usuarios u INNER JOIN Roles r ON r.RolId=u.RolId WHERE r.Nombre='Administrador'", connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public static int GetAdministratorRoleId(MySqlConnection connection)
    {
        using var command = new MySqlCommand("SELECT RolId FROM Roles WHERE Nombre='Administrador' AND Activo=TRUE LIMIT 1", connection);
        object? value = command.ExecuteScalar();
        if (value is null || value == DBNull.Value) throw new InvalidOperationException("No se encontró el rol Administrador activo.");
        return Convert.ToInt32(value);
    }

    public static AppUser? Authenticate(string email, string password) => SecurityService.Authenticate(email, password);
    public static AppUser? AuthenticateCore(string email, string password)
    {
        using var connection = new MySqlConnection(DatabaseSettings.ConnectionString);
        connection.Open();
        const string sql = "SELECT u.UsuarioId,u.Nombre,u.Correo,u.HashContrasena,u.Activo,u.RolId,r.Nombre AS Rol FROM Usuarios u INNER JOIN Roles r ON r.RolId=u.RolId WHERE u.Correo=@correo AND r.Activo=TRUE LIMIT 1";
        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@correo", email.Trim());
        int id, roleId;
        string name, storedHash, roleName;
        bool active;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) return null;
            id = reader.GetInt32("UsuarioId");
            name = reader.GetString("Nombre");
            storedHash = reader.GetString("HashContrasena");
            active = reader.GetBoolean("Activo");
            roleId = reader.GetInt32("RolId");
            roleName = reader.GetString("Rol");
        }
        if (!active || !PasswordHasher.Verify(password, storedHash)) return null;

        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var permissionCommand = new MySqlCommand(
            "SELECT p.Codigo FROM RolesPermisos rp INNER JOIN Permisos p ON p.PermisoId=rp.PermisoId INNER JOIN Roles r ON r.RolId=rp.RolId WHERE rp.RolId=@rol AND r.Activo=TRUE",
            connection))
        {
            permissionCommand.Parameters.AddWithValue("@rol", roleId);
            using var reader = permissionCommand.ExecuteReader();
            while (reader.Read()) permissions.Add(reader.GetString("Codigo"));
        }
        AuditLog.Write(connection, null, "Iniciar sesión", "Usuarios", id, id);
        return new AppUser(id, name, email.Trim(), roleId, roleName, permissions);
    }
}

internal static class PasswordHasher
{
    private const int PasswordIterations = 310_000;

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
        return "pbkdf2_sha256$" + PasswordIterations.ToString() + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
    }

    public static bool Verify(string password, string encoded)
    {
        try
        {
            string[] parts = encoded.Split('$');
            if (parts.Length != 4 || parts[0] != "pbkdf2_sha256" ||
                !int.TryParse(parts[1], out int iterations) || iterations < 10_000 || iterations > 1_000_000) return false;
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            if (salt.Length < 16 || expected.Length < 16) return false;
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
        catch (CryptographicException) { return false; }
    }
}

internal sealed class InitialAdminForm : StudioForm
{
    private readonly TextBox nameInput = new();
    private readonly TextBox emailInput = new();
    private readonly TextBox passwordInput = new() { UseSystemPasswordChar = true };
    private readonly TextBox confirmInput = new() { UseSystemPasswordChar = true };
    private readonly Label error = new() { AutoSize = true, ForeColor = Color.Firebrick };
    private readonly Action created;

    public InitialAdminForm(Action created)
    {
        this.created = created;
        Text = "Integra Gym | Crear administrador";
        Size = new Size(510, 480); MinimumSize = new Size(510, 480);
        StartPosition = FormStartPosition.CenterParent; BackColor = Color.FromArgb(244,247,251);
        Controls.Add(new Label { Text = "Primera configuración", Font = new Font("Segoe UI", 21, FontStyle.Bold), AutoSize = true, Location = new Point(28, 22) });
        Controls.Add(new Label { Text = "Crea la primera cuenta administradora de Integra Gym.", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(31, 62) });
        AddField("Nombre completo", nameInput, 105);
        AddField("Correo de acceso", emailInput, 165);
        AddField("Contraseña (mínimo 12 caracteres)", passwordInput, 225);
        AddField("Confirmar contraseña", confirmInput, 285);
        error.Location = new Point(32, 350); Controls.Add(error);
        var save = new Button { Text = "Crear administrador", Location = new Point(30, 380), Size = new Size(180, 36), BackColor = Color.FromArgb(57,97,170), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        save.Click += (_, _) => CreateAccount(); Controls.Add(save);
        var cancel = new Button { Text = "Cancelar", Location = new Point(220, 380), Size = new Size(110, 36), FlatStyle = FlatStyle.Flat };
        cancel.Click += (_, _) => Close(); Controls.Add(cancel);
    }

    private void AddField(string label, TextBox box, int y)
    {
        Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(32, y), ForeColor = Color.FromArgb(102,117,136) });
        box.Location = new Point(32, y + 24); box.Width = 420; Controls.Add(box);
    }

    private void CreateAccount()
    {
        string name = nameInput.Text.Trim(), email = emailInput.Text.Trim(), password = passwordInput.Text;
        if (name.Length < 2) { error.Text = "Escribe el nombre completo."; nameInput.Focus(); return; }
        if (!MailAddress.TryCreate(email, out _)) { error.Text = "Escribe un correo válido."; emailInput.Focus(); return; }
        if (password.Length < 12) { error.Text = "Usa una contraseña de al menos 12 caracteres."; passwordInput.Focus(); return; }
        if (password != confirmInput.Text) { error.Text = "Las contraseñas no coinciden."; confirmInput.Focus(); return; }
        try
        {
            using var connection = new MySqlConnection(DatabaseSettings.ConnectionString); connection.Open();
            AccessControl.Initialize(connection);
            if (AccessControl.GetAdministratorCount(connection) != 0) { error.Text = "Ya existe una cuenta administradora. Inicia sesión con ella."; return; }
            using var command = new MySqlCommand("INSERT INTO Usuarios(RolId,Nombre,Correo,HashContrasena,Activo) VALUES(@rol,@nombre,@correo,@hash,TRUE)", connection);
            command.Parameters.AddWithValue("@rol", AccessControl.GetAdministratorRoleId(connection));
            command.Parameters.AddWithValue("@nombre", name);
            command.Parameters.AddWithValue("@correo", email);
            command.Parameters.AddWithValue("@hash", PasswordHasher.Hash(password));
            using var transaction = connection.BeginTransaction();
            using (var roleLock = new MySqlCommand("SELECT RolId FROM Roles WHERE Nombre='Administrador' FOR UPDATE", connection, transaction)) roleLock.ExecuteScalar();
            using (var count = new MySqlCommand("SELECT COUNT(*) FROM Usuarios u JOIN Roles r ON r.RolId=u.RolId WHERE r.Nombre='Administrador'", connection, transaction))
                if(Convert.ToInt32(count.ExecuteScalar()) != 0) { error.Text = "Ya se creó el administrador. Vuelve al inicio de sesión."; return; }
            command.Transaction = transaction;
            command.ExecuteNonQuery();
            AuditLog.Write(connection, transaction, "Crear administrador inicial", "Usuarios", command.LastInsertedId, checked((int)command.LastInsertedId));
            transaction.Commit();
            MessageBox.Show("Cuenta administradora creada. Ya puedes iniciar sesión.", "Integra Gym", MessageBoxButtons.OK, MessageBoxIcon.Information);
            created(); Close();
        }
        catch (MySqlException ex) { error.Text = "No se pudo crear la cuenta: " + ex.Message; }
        catch (InvalidOperationException ex) { error.Text = ex.Message; }
    }
}

internal sealed class LoginForm : StudioForm
{
    private readonly TextBox emailInput = new();
    private readonly TextBox passwordInput = new() { UseSystemPasswordChar = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(450, 0), ForeColor = Color.DimGray };
    private readonly Button loginButton = new() { Text = "Iniciar sesión", Size = new Size(140, 36), BackColor = Color.FromArgb(57,97,170), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private readonly Button createAdminButton = new() { Text = "Crear administrador inicial", Size = new Size(210, 36), FlatStyle = FlatStyle.Flat };
    private bool setupRequired;
    public AppUser? AuthenticatedUser { get; private set; }

    public LoginForm()
    {
        Text = "Integra Gym | Iniciar sesión"; Size = new Size(600, 570); MinimumSize = new Size(600, 570);
        StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(244,247,251);
        Controls.Add(new Label { Text = "Integra Gym", Font = new Font("Segoe UI", 24, FontStyle.Bold), AutoSize = true, Location = new Point(34, 30) });
        Controls.Add(new Label { Text = "Inicia sesión con tu cuenta del gimnasio.", Font = new Font("Segoe UI", 10), ForeColor = Color.DimGray, AutoSize = true, Location = new Point(38, 75) });
        AddField("Correo", emailInput, 130); AddField("Contraseña", passwordInput, 200);
        loginButton.Location = new Point(36, 275); loginButton.Click += (_, _) => SignIn(); Controls.Add(loginButton);
        createAdminButton.Location = new Point(190, 275); createAdminButton.Click += (_, _) => ShowInitialSetup(); createAdminButton.Visible = false; Controls.Add(createAdminButton);
        status.Location = new Point(38, 330); Controls.Add(status);
        var configure = new Button { Text = "Configurar conexión…", Location = new Point(38, 410), Size = new Size(220, 35) }; configure.Click += (_, _) => { using var f = new SetupForm(); f.ShowDialog(this); CheckDatabase(); }; Controls.Add(configure);
        var recover = new Button { Text = "Recuperar administrador…", Location = new Point(270, 410), Size = new Size(240, 35) }; recover.Click += (_, _) => { using var f = new PasswordForm(true); f.ShowDialog(this); }; Controls.Add(recover);
        AcceptButton = loginButton;
        Load += (_, _) => CheckDatabase();
    }

    private void AddField(string label, TextBox box, int y)
    {
        Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(38, y), ForeColor = Color.FromArgb(102,117,136) });
        box.Location = new Point(38, y + 25); box.Width = 450; Controls.Add(box);
    }

    private void CheckDatabase()
    {
        if (string.IsNullOrWhiteSpace(DatabaseSettings.ConnectionString))
        {
            status.Text = "Usa Configurar conexión para conectar con MySQL.";
            loginButton.Enabled = false; createAdminButton.Enabled = false; return;
        }
        try
        {
            using var connection = new MySqlConnection(DatabaseSettings.ConnectionString); connection.Open();
            if (!SchemaUpgrade.Ready(connection)) { status.Text = "Actualiza la base a v1.2 desde Configurar conexión."; loginButton.Enabled = false; createAdminButton.Enabled = false; return; }
            createAdminButton.Enabled = true;
            AccessControl.Initialize(connection);
            setupRequired = AccessControl.GetAdministratorCount(connection) == 0;
            createAdminButton.Visible = setupRequired;
            createAdminButton.BringToFront();
            loginButton.Enabled = !setupRequired;
            status.Text = setupRequired ? "No hay administrador. Crea la primera cuenta administradora." : "Conectado a la base local.";
        }
        catch (MySqlException ex)
        {
            status.Text = "No se pudo conectar a MySQL. Confirma que el servidor esté iniciado y la conexión configurada.";
            loginButton.Enabled = false; createAdminButton.Enabled = false;
            MessageBox.Show("No se pudo preparar el inicio de sesión.\n\n" + ex.Message, "Conexión a MySQL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowInitialSetup()
    {
        if (!setupRequired) return;
        using var setup = new InitialAdminForm(CheckDatabase);
        setup.ShowDialog(this);
    }

    private void SignIn()
    {
        string email = emailInput.Text.Trim(), password = passwordInput.Text;
        if (!MailAddress.TryCreate(email, out _)) { status.Text = "Escribe un correo válido."; emailInput.Focus(); return; }
        if (password.Length == 0) { status.Text = "Escribe tu contraseña."; passwordInput.Focus(); return; }
        try
        {
            var user = AccessControl.Authenticate(email, password);
            if (user is null) { status.Text = "Acceso no válido. Tras 5 intentos fallidos espera 15 minutos."; passwordInput.Clear(); passwordInput.Focus(); return; }
            AuthenticatedUser = user; DialogResult = DialogResult.OK; Close();
        }
        catch (MySqlException ex)
        {
            status.Text = "No se pudo validar la cuenta en MySQL.";
            MessageBox.Show(ex.Message, "Error de base de datos", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}




