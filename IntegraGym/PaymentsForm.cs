using MySqlConnector;
using System.Data;
using System.Globalization;

namespace IntegraGym;

internal sealed class PaymentsForm : StudioForm
{
    private static string ConnectionString => DatabaseSettings.ConnectionString;
    private readonly ComboBox clientCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox membershipCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox methodCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown amountInput = new() { Minimum = 0.01m, Maximum = 1000000m, DecimalPlaces = 2, ThousandsSeparator = true };
    private readonly DateTimePicker paymentDate = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm" };
    private readonly Label balanceLabel = new() { AutoSize = true, ForeColor = Color.FromArgb(57,97,170) };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly DataGridView grid = new();
    private bool loadingCatalogs;

    private sealed record MembershipChoice(int? Id, string Label, decimal? Balance);

    public PaymentsForm()
    {
        Text = "Integra Gym | Pagos"; MinimumSize = new Size(1080, 690); StartPosition = FormStartPosition.CenterParent; WindowState = FormWindowState.Maximized; BackColor = Color.FromArgb(244,247,251);
        BuildLayout(); AppUi.StyleForm(this);
        Load += (_, _) => { LoadCatalogs(); LoadPayments(); };
    }

    private void BuildLayout()
    {
        Controls.Add(new Label { Text = "Pagos", Font = new Font("Segoe UI", 22, FontStyle.Bold), AutoSize = true, Location = new Point(28, 18) });
        Controls.Add(new Label { Text = "Registra cobros y consulta el historial de transacciones.", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(31, 58) });
        var editor = new Panel { Location = new Point(28, 90), Size = new Size(950, 174), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.White };
        AddLabel(editor, "Cliente", 15, 12); clientCombo.Location = new Point(15, 38); clientCombo.Width = 220; editor.Controls.Add(clientCombo);
        AddLabel(editor, "Membresía (opcional)", 255, 12); membershipCombo.Location = new Point(255, 38); membershipCombo.Width = 255; editor.Controls.Add(membershipCombo);
        AddLabel(editor, "Método de pago", 530, 12); methodCombo.Location = new Point(530, 38); methodCombo.Width = 170; editor.Controls.Add(methodCombo);
        AddLabel(editor, "Importe (MXN)", 720, 12); amountInput.Location = new Point(720, 38); amountInput.Width = 150; editor.Controls.Add(amountInput);
        AddLabel(editor, "Fecha y hora", 15, 88); paymentDate.Location = new Point(15, 114); paymentDate.Width = 190; editor.Controls.Add(paymentDate);
        balanceLabel.Location = new Point(255, 119); editor.Controls.Add(balanceLabel);
        var save = MakeButton("Registrar pago", 720, 103, true); save.Size = new Size(150, 36); save.Click += (_, _) => RegisterPayment(); editor.Controls.Add(save);
        Controls.Add(editor);
        clientCombo.SelectionChangeCommitted += (_, _) => LoadClientMemberships();
        membershipCombo.SelectionChangeCommitted += (_, _) => UpdateMembershipBalance();
        grid.Location = new Point(28, 285); grid.Size = new Size(950, 270); grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grid.ReadOnly = true; AppUi.ConfigureGrid(grid); grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.AllowUserToAddRows = false; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None; Controls.Add(grid);
        var refresh = MakeButton("Actualizar historial", 28, 570, false); refresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; refresh.Click += (_, _) => LoadPayments(); Controls.Add(refresh);
        var catalogs = MakeButton("Actualizar clientes", 165, 570, false); catalogs.Width = 150; catalogs.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; catalogs.Click += (_, _) => { if (MessageBox.Show(this, "Se recargarán los clientes, métodos y membresías. ¿Continuar?", "Actualizar catálogos", MessageBoxButtons.YesNo) == DialogResult.Yes) LoadCatalogs(); }; Controls.Add(catalogs);
        status.Location = new Point(330, 578); status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; Controls.Add(status);
    }

    private static void AddLabel(Control parent, string text, int x, int y) => parent.Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = Color.FromArgb(102,117,136) });

    private static Button MakeButton(string text, int x, int y, bool primary) => new() { Text = text, Location = new Point(x, y), Size = new Size(100, 30), FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(57,97,170) : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(26,39,57), Cursor = Cursors.Hand };

    private void LoadCatalogs()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) { MessageBox.Show("Configura la conexión local de MySQL antes de registrar pagos."); return; }
        loadingCatalogs = true;
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open();
            var clients = ReadTable(connection, "SELECT ClienteId, NombreCompleto FROM Clientes WHERE Estado='Activo' AND DatosPurgados=FALSE ORDER BY NombreCompleto");
            clientCombo.DataSource = clients; clientCombo.DisplayMember = "NombreCompleto"; clientCombo.ValueMember = "ClienteId";
            var methods = ReadTable(connection, "SELECT MetodoPagoId, Nombre FROM MetodosPago WHERE Activo=TRUE ORDER BY Nombre");
            methodCombo.DataSource = methods; methodCombo.DisplayMember = "Nombre"; methodCombo.ValueMember = "MetodoPagoId";
        }
        catch (MySqlException ex) { ShowDbError("No se pudieron cargar clientes o métodos de pago.", ex); }
        finally { loadingCatalogs = false; }
        LoadClientMemberships();
    }

    private static DataTable ReadTable(MySqlConnection connection, string sql)
    {
        using var command = new MySqlCommand(sql, connection); using var reader = command.ExecuteReader(); var table = new DataTable(); table.Load(reader); return table;
    }

    private void LoadClientMemberships()
    {
        if (loadingCatalogs || clientCombo.SelectedValue is null) return;
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open();
            const string sql = "SELECT m.MembresiaId, p.Nombre, m.FechaVencimiento, m.EstadoPago, fn_SaldoMembresia(m.MembresiaId) AS Saldo FROM Membresias m INNER JOIN Planes p ON p.PlanId=m.PlanId WHERE m.ClienteId=@cliente AND m.Estado<>'Cancelada' AND m.EstadoPago<>'Pagada' ORDER BY m.MembresiaId DESC";
            using var command = new MySqlCommand(sql, connection); command.Parameters.AddWithValue("@cliente", Convert.ToInt32(clientCombo.SelectedValue)); using var reader = command.ExecuteReader();
            var choices = new List<MembershipChoice> { new(null, "Sin membresía", null) };
            while (reader.Read())
            {
                int id = reader.GetInt32("MembresiaId"); string plan = reader.GetString("Nombre"); DateTime expiry = reader.GetDateTime("FechaVencimiento"); string paid = reader.GetString("EstadoPago"); decimal balance = reader.IsDBNull("Saldo") ? 0 : reader.GetDecimal("Saldo");
                choices.Add(new MembershipChoice(id, $"{plan} · vence {expiry:dd/MM/yyyy} · {paid}", balance));
            }
            membershipCombo.DataSource = choices; membershipCombo.DisplayMember = nameof(MembershipChoice.Label); membershipCombo.SelectedIndex = 0;
            UpdateMembershipBalance();
        }
        catch (MySqlException ex) { ShowDbError("No se pudieron consultar las membresías del cliente.", ex); }
    }

    private void UpdateMembershipBalance()
    {
        if (membershipCombo.SelectedItem is not MembershipChoice choice || choice.Id is null || choice.Balance is null)
        {
            balanceLabel.Text = "Este pago no se asignará a una membresía."; return;
        }
        balanceLabel.Text = $"Saldo pendiente: {choice.Balance.Value.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))}";
        if (choice.Balance.Value > 0) amountInput.Value = Math.Min(choice.Balance.Value, amountInput.Maximum);
    }

    private void LoadPayments()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;
        try
        {
            using var connection = new MySqlConnection(ConnectionString); connection.Open();
            const string sql = "SELECT p.PagoId, c.NombreCompleto AS Cliente, pl.Nombre AS Plan, mp.Nombre AS Metodo, p.Importe, p.FechaPago, p.Estado FROM Pagos p INNER JOIN Clientes c ON c.ClienteId=p.ClienteId LEFT JOIN Membresias m ON m.MembresiaId=p.MembresiaId LEFT JOIN Planes pl ON pl.PlanId=m.PlanId INNER JOIN MetodosPago mp ON mp.MetodoPagoId=p.MetodoPagoId ORDER BY p.FechaPago DESC, p.PagoId DESC";
            grid.DataSource = ReadTable(connection, sql);
            var idColumn = grid.Columns["PagoId"]; if (idColumn is not null) idColumn.Visible = false;
            if (grid.Columns["Importe"] is DataGridViewColumn amountColumn) { amountColumn.HeaderText = "Importe (MXN)"; amountColumn.DefaultCellStyle.Format = "C2"; amountColumn.DefaultCellStyle.FormatProvider = CultureInfo.GetCultureInfo("es-MX"); }
            status.Text = $"{grid.Rows.Count} pagos";
        }
        catch (MySqlException ex) { ShowDbError("No se pudo cargar el historial de pagos.", ex); }
    }

    private void RegisterPayment()
    {
        if (clientCombo.SelectedValue is null || methodCombo.SelectedValue is null) { MessageBox.Show("Selecciona un cliente y un método de pago."); return; }
        MembershipChoice choice = membershipCombo.SelectedItem as MembershipChoice ?? new MembershipChoice(null, "Sin membresía", null);
        if (amountInput.Value <= 0) { MessageBox.Show("El importe debe ser mayor que cero."); return; }
        if (choice.Balance is decimal balance && amountInput.Value > balance) { MessageBox.Show("El importe supera el saldo pendiente de la membresía."); return; }
        try
        {
            GymOperations.RegisterPayment(Convert.ToInt32(clientCombo.SelectedValue), choice.Id,
                Convert.ToInt32(methodCombo.SelectedValue), amountInput.Value, paymentDate.Value);
            LoadPayments(); LoadClientMemberships();
            MessageBox.Show("Pago registrado correctamente.", "Integra Gym", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (MySqlException ex) { ShowDbError("El pago no se registró. MySQL rechazó la operación y no mostró una confirmación falsa.", ex); }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { MessageBox.Show(ex.Message, "Pago no registrado", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    private static void ShowDbError(string message, MySqlException ex) => MessageBox.Show($"{message}\n\n{ex.Message}", "Error de base de datos", MessageBoxButtons.OK, MessageBoxIcon.Error);
}



