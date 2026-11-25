using System.Drawing;
using System.Windows.Forms;

namespace IntegraGym;

internal static class AppUi
{
    private static readonly Dictionary<string, string> HeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NombreCompleto"] = "Nombre completo",
        ["Telefono"] = "Teléfono",
        ["FechaRegistro"] = "Fecha de registro",
        ["FechaHora"] = "Fecha y hora",
        ["Accion"] = "Acción",
        ["Entidad"] = "Módulo",
        ["Registro"] = "Registro afectado",
        ["FechaAlta"] = "Fecha de alta",
        ["FechaActualizacion"] = "Última actualización",
        ["FechaInactivacion"] = "Fecha de baja",
        ["EstadoPago"] = "Estado de pago",
        ["DuracionDias"] = "Duración (días)",
        ["PrecioAcordado"] = "Precio acordado",
        ["FechaInicio"] = "Inicio",
        ["FechaVencimiento"] = "Vencimiento",
        ["FechaPago"] = "Fecha de pago",
        ["Metodo"] = "Método de pago",
        ["MetodoPago"] = "Método de pago",
        ["Activo"] = "Activo",
        ["Rol"] = "Rol",
        ["Descripcion"] = "Descripción",
        ["Modulo"] = "Módulo",
        ["Asignado"] = "Acceso",
        ["NumeroPagos"] = "No. de pagos",
        ["SaldoPendiente"] = "Saldo pendiente",
        ["Ingresos"] = "Ingresos",
        ["MembresiasVendidas"] = "Membresías vendidas",
        ["ImporteAcordado"] = "Importe acordado",
        ["Estado"] = "Estado",
        ["Plan"] = "Plan",
        ["Cliente"] = "Cliente",
        ["Fecha"] = "Fecha",
        ["Nombre"] = "Nombre",
        ["Correo"] = "Correo",
        ["Precio"] = "Precio",
        ["Importe"] = "Importe",
        ["Saldo"] = "Saldo",
        ["Cantidad"] = "Cantidad"
    };

    public static void StyleForm(Form form)
    {
        form.Font = new Font("Segoe UI", 10, FontStyle.Regular);
        form.BackColor = Color.FromArgb(244,247,251);
        StyleControls(form.Controls);
    }

    private static void StyleControls(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            switch (control)
            {
                case TextBox textBox:
                    textBox.Font = new Font("Segoe UI", 10);
                    textBox.BackColor = Color.White;
                    textBox.ForeColor = Color.FromArgb(26,39,57);
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ComboBox comboBox:
                    comboBox.Font = new Font("Segoe UI", 10);
                    comboBox.BackColor = Color.White;
                    break;
                case NumericUpDown numeric:
                    numeric.Font = new Font("Segoe UI", 10);
                    numeric.BackColor = Color.White;
                    break;
                case CheckBox checkBox:
                    checkBox.Font = new Font("Segoe UI", 9.5f);
                    checkBox.ForeColor = Color.FromArgb(76,91,112);
                    break;
                case Button button:
                    button.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 1;
                    bool primary = button.BackColor.ToArgb() == Theme.Accent.ToArgb();
                    button.FlatAppearance.BorderColor = primary ? Color.FromArgb(57,97,170) : Color.FromArgb(218,226,239);
                    button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(44,78,144) : Color.FromArgb(237,242,252);
                    button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(34,61,117) : Color.FromArgb(222,232,249);
                    button.Cursor = Cursors.Hand;
                    break;
                case DataGridView grid:
                    grid.Font = new Font("Segoe UI", 9.5f);
                    break;
            }

            if (control.HasChildren)
                StyleControls(control.Controls);
        }
    }
    public static void ConfigureGrid(DataGridView grid)
    {
        grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
        grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None;
        grid.ColumnHeadersDefaultCellStyle.Font=new Font(grid.Font,FontStyle.Bold);
        GymTableStyle.Apply(grid);
        grid.DataBindingComplete += (_, _) => FormatBoundColumns(grid);
    }

    public static void FormatBoundColumns(DataGridView grid)
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (column.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("DatosPurgados", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("HashContrasena", StringComparison.OrdinalIgnoreCase))
            {
                column.Visible = false;
                continue;
            }

            if (HeaderNames.TryGetValue(column.Name, out var header))
                column.HeaderText = header;

            column.MinimumWidth = MinimumWidth(column.Name);
            column.FillWeight = FillWeight(column.Name);
            column.SortMode = DataGridViewColumnSortMode.Automatic;
            if (column.ValueType == typeof(bool))
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
    }

    private static int MinimumWidth(string name)
    {
        if (name.Contains("Descripcion", StringComparison.OrdinalIgnoreCase)) return 220;
        if (name.Contains("Correo", StringComparison.OrdinalIgnoreCase)) return 170;
        if (name.Contains("Fecha", StringComparison.OrdinalIgnoreCase) || name.Contains("Vencimiento", StringComparison.OrdinalIgnoreCase)) return 135;
        if (name.Contains("Telefono", StringComparison.OrdinalIgnoreCase)) return 120;
        if (name.Contains("Estado", StringComparison.OrdinalIgnoreCase) || name.Contains("Activo", StringComparison.OrdinalIgnoreCase)) return 95;
        return 90;
    }

    private static float FillWeight(string name)
    {
        if (name.Contains("Nombre", StringComparison.OrdinalIgnoreCase) || name.Equals("Cliente", StringComparison.OrdinalIgnoreCase) || name.Equals("Plan", StringComparison.OrdinalIgnoreCase)) return 24;
        if (name.Contains("Correo", StringComparison.OrdinalIgnoreCase) || name.Contains("Descripcion", StringComparison.OrdinalIgnoreCase)) return 25;
        if (name.Contains("Fecha", StringComparison.OrdinalIgnoreCase) || name.Contains("Vencimiento", StringComparison.OrdinalIgnoreCase)) return 16;
        if (name.Contains("Telefono", StringComparison.OrdinalIgnoreCase)) return 14;
        if (name.Contains("Importe", StringComparison.OrdinalIgnoreCase) || name.Contains("Precio", StringComparison.OrdinalIgnoreCase) || name.Contains("Saldo", StringComparison.OrdinalIgnoreCase) || name.Contains("Ingreso", StringComparison.OrdinalIgnoreCase)) return 13;
        if (name.Contains("Estado", StringComparison.OrdinalIgnoreCase) || name.Contains("Activo", StringComparison.OrdinalIgnoreCase) || name.Equals("Rol", StringComparison.OrdinalIgnoreCase)) return 11;
        return 16;
    }
}




