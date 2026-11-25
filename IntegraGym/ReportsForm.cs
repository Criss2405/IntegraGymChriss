using MySqlConnector;
using System.Data;
using System.Globalization;
using System.Text;

namespace IntegraGym;

internal sealed class ReportsForm : StudioForm
{
    private static string ConnectionString => DatabaseSettings.ConnectionString;
    private readonly ComboBox reportCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker fromDate = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy" };
    private readonly DateTimePicker throughDate = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy" };
    private readonly DataGridView grid = new();
    private readonly Label summary = new() { AutoSize = true, ForeColor = Color.FromArgb(57,97,170) };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private DataTable? currentReport;

    private sealed record ReportOption(string Name, string Sql, bool UsesDates, string SummaryLabel)
    {
        public override string ToString() => Name;
    }

    private static readonly ReportOption[] Options =
    [
        new("Ingresos por periodo", """
            SELECT DATE(p.FechaPago) AS Fecha, COUNT(*) AS NumeroPagos, SUM(p.Importe) AS Ingresos
            FROM Pagos p
            WHERE p.Estado='Aplicado' AND p.FechaPago>=@desde AND p.FechaPago<DATE_ADD(@hasta, INTERVAL 1 DAY)
            GROUP BY DATE(p.FechaPago) ORDER BY Fecha DESC
            """, true, "ingresos"),
        new("Membresías vencidas", """
            SELECT c.NombreCompleto AS Cliente, p.Nombre AS Plan, m.FechaInicio, m.FechaVencimiento,
                   m.EstadoPago, fn_SaldoMembresia(m.MembresiaId) AS SaldoPendiente
            FROM Membresias m
            INNER JOIN Clientes c ON c.ClienteId=m.ClienteId
            INNER JOIN Planes p ON p.PlanId=m.PlanId
            WHERE m.Estado<>'Cancelada' AND m.FechaVencimiento<CURDATE()
            ORDER BY m.FechaVencimiento DESC
            """, false, "membresías vencidas"),
        new("Clientes activos", """
            SELECT ClienteId, NombreCompleto, Correo, Telefono, FechaRegistro
            FROM Clientes WHERE Estado='Activo' AND DatosPurgados=FALSE
            ORDER BY NombreCompleto
            """, false, "clientes activos"),
        new("Ventas de planes", """
            SELECT pl.Nombre AS Plan, COUNT(*) AS MembresiasVendidas,
                   SUM(m.PrecioAcordado) AS ImporteAcordado
            FROM Membresias m INNER JOIN Planes pl ON pl.PlanId=m.PlanId
            WHERE m.FechaInicio>=@desde AND m.FechaInicio<DATE_ADD(@hasta, INTERVAL 1 DAY)
              AND m.Estado<>'Cancelada'
            GROUP BY pl.PlanId, pl.Nombre ORDER BY MembresiasVendidas DESC, pl.Nombre
            """, true, "membresías vendidas")
    ];

    public ReportsForm()
    {
        Text = "Integra Gym | Reportes";
        MinimumSize = new Size(1040, 650);
        StartPosition = FormStartPosition.CenterParent; WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(244,247,251);
        BuildLayout(); AppUi.StyleForm(this);
        Load += (_, _) => LoadReport();
    }

    private void BuildLayout()
    {
        Controls.Add(new Label { Text = "Reportes", Font = new Font("Segoe UI", 22, FontStyle.Bold), AutoSize = true, Location = new Point(28, 18) });
        Controls.Add(new Label { Text = "Consulta ingresos, vencimientos, clientes activos y ventas de planes.", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(31, 58) });
        var filters = new Panel { Location = new Point(28, 94), Size = new Size(950, 112), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.White };
        AddLabel(filters, "Reporte", 15, 14); reportCombo.Location = new Point(15, 40); reportCombo.Width = 255; reportCombo.DataSource = Options; filters.Controls.Add(reportCombo);
        AddLabel(filters, "Desde", 290, 14); fromDate.Location = new Point(290, 40); fromDate.Width = 125; filters.Controls.Add(fromDate);
        AddLabel(filters, "Hasta", 435, 14); throughDate.Location = new Point(435, 40); throughDate.Width = 125; filters.Controls.Add(throughDate);
        var generate = MakeButton("Generar", 590, 36, true); generate.Click += (_, _) => LoadReport(); filters.Controls.Add(generate);
        var export = MakeButton("Exportar CSV", 700, 36, false); export.Width = 120; export.Click += (_, _) => ExportReport(); filters.Controls.Add(export);
        Controls.Add(filters);
        summary.Location = new Point(30, 222); Controls.Add(summary);
        grid.Location = new Point(28, 254); grid.Size = new Size(950, 290); grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grid.ReadOnly = true; AppUi.ConfigureGrid(grid); grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.AllowUserToAddRows = false; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None; grid.RowHeadersVisible = false;
        Controls.Add(grid);
        var refresh = MakeButton("Actualizar", 28, 560, false); refresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; refresh.Click += (_, _) => LoadReport(); Controls.Add(refresh);
        status.Location = new Point(145, 568); status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; Controls.Add(status);
        reportCombo.SelectedIndexChanged += (_, _) => UpdateDateControls();
        fromDate.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        throughDate.Value = DateTime.Today;
        UpdateDateControls();
    }

    private static void AddLabel(Control parent, string text, int x, int y) => parent.Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = Color.FromArgb(102,117,136) });
    private static Button MakeButton(string text, int x, int y, bool primary) => new()
    {
        Text = text, Location = new Point(x, y), Size = new Size(100, 34), FlatStyle = FlatStyle.Flat,
        BackColor = primary ? Color.FromArgb(57,97,170) : Color.White,
        ForeColor = primary ? Color.White : Color.FromArgb(26,39,57), Cursor = Cursors.Hand
    };

    private void UpdateDateControls()
    {
        bool enabled = (reportCombo.SelectedItem as ReportOption)?.UsesDates == true;
        fromDate.Enabled = enabled;
        throughDate.Enabled = enabled;
    }

    private void LoadReport()
    {
        if (reportCombo.SelectedItem is not ReportOption option) return;
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            MessageBox.Show("Configura la conexión local de MySQL antes de consultar reportes.", "Base de datos", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (option.UsesDates && fromDate.Value.Date > throughDate.Value.Date)
        {
            MessageBox.Show("La fecha inicial no puede ser posterior a la fecha final.", "Revisa el periodo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            AppSession.Require("reportes.ver");
            using var connection = new MySqlConnection(ConnectionString);
            connection.Open();
            using var command = new MySqlCommand(option.Sql, connection);
            if (option.UsesDates)
            {
                command.Parameters.AddWithValue("@desde", fromDate.Value.Date);
                command.Parameters.AddWithValue("@hasta", throughDate.Value.Date);
            }
            using var reader = command.ExecuteReader();
            var table = new DataTable();
            table.Load(reader);
            currentReport = table;
            grid.DataSource = table;
            FormatColumns();
            summary.Text = BuildSummary(option, table);
            status.Text = $"{table.Rows.Count} filas";
        }
        catch (Exception ex) when (ex is MySqlException or UnauthorizedAccessException)
        {
            currentReport = null;
            grid.DataSource = null;
            summary.Text = "";
            status.Text = "No se pudo generar el reporte.";
            MessageBox.Show($"No se pudo consultar MySQL. Confirma que el servidor esté iniciado y que la base de datos tenga el esquema Integra Gym.\n\n{ex.Message}", "Error de base de datos", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void FormatColumns()
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (column.ValueType == typeof(DateTime)) column.DefaultCellStyle.Format = "dd/MM/yyyy";
            if (column.Name.Contains("Importe", StringComparison.OrdinalIgnoreCase) || column.Name.Contains("Saldo", StringComparison.OrdinalIgnoreCase) || column.Name.Equals("Ingresos", StringComparison.OrdinalIgnoreCase))
            {
                column.DefaultCellStyle.Format = "C2";
                column.DefaultCellStyle.FormatProvider = CultureInfo.GetCultureInfo("es-MX");
            }
        }
        if (grid.Columns["ClienteId"] is DataGridViewColumn clientId) clientId.Visible = false;
    }

    private static string BuildSummary(ReportOption option, DataTable table)
    {
        if (option.Name == "Ingresos por periodo")
        {
            decimal total = table.Rows.Cast<DataRow>().Sum(row => row["Ingresos"] == DBNull.Value ? 0m : Convert.ToDecimal(row["Ingresos"]));
            return $"Total del periodo: {total.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))}";
        }
        if (option.Name == "Ventas de planes")
        {
            int total = table.Rows.Cast<DataRow>().Sum(row => row["MembresiasVendidas"] == DBNull.Value ? 0 : Convert.ToInt32(row["MembresiasVendidas"]));
            return $"Total del periodo: {total} {option.SummaryLabel}";
        }
        return $"Total: {table.Rows.Count} {option.SummaryLabel}";
    }

    private void ExportReport()
    {
        if (currentReport is null || currentReport.Rows.Count == 0)
        {
            MessageBox.Show("Genera un reporte con resultados antes de exportar.", "Sin datos", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog { Filter = "Archivo CSV (*.csv)|*.csv", DefaultExt = "csv", AddExtension = true, FileName = $"reporte-integra-gym-{DateTime.Today:yyyy-MM-dd}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            AppSession.Require("reportes.ver");
            using var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true));
            writer.WriteLine(string.Join(";", currentReport.Columns.Cast<DataColumn>().Select(column => EscapeCsv(column.ColumnName))));
            foreach (DataRow row in currentReport.Rows)
                writer.WriteLine(string.Join(";", currentReport.Columns.Cast<DataColumn>().Select(column => EscapeCsv(FormatCsvValue(row[column])))));
            writer.Flush();
            status.Text = $"Reporte exportado: {Path.GetFileName(dialog.FileName)}";
            AuditLog.Record("Exportar reporte CSV", "Reportes");
        }
        catch (IOException ex)
        {
            MessageBox.Show($"No se pudo guardar el archivo.\n\n{ex.Message}", "Error al exportar", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (UnauthorizedAccessException ex) { MessageBox.Show(this, ex.Message, "No se pudo exportar", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        catch (MySqlException) { MessageBox.Show("El reporte se exportó, pero no se pudo registrar la acción en la bitácora.", "Bitácora", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private static string FormatCsvValue(object value) => value switch
    {
        DBNull => "",
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };
    private static string EscapeCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}


