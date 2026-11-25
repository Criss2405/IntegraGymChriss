using MySqlConnector;
using System.Data;

namespace IntegraGym;

internal sealed record HistoryPage(DataTable Rows, long Total, long Number, long Snapshot)
{
    public long Pages => Math.Max(1, (Total + HistoryPages.PageSize - 1) / HistoryPages.PageSize);
}

internal static class HistoryPages
{
    public const int PageSize = 250;

    // Capture the last ID on a new search so newly arriving entries do not shift pages.
    public static HistoryPage Read(bool audit, DateTime from, DateTime through, string search,
        int? client, long page = 0, long? snapshot = null)
    {
        AppSession.Require(audit ? "bitacora.ver" : "asistencias.gestionar");
        if (from.Date > through.Date) throw new InvalidOperationException("Revisa el intervalo de fechas.");
        using var connection = SecurityService.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.RepeatableRead);
        string table = audit ? "Bitacora" : "Asistencias";
        string id = audit ? "b.BitacoraId" : "b.AsistenciaId";
        using var max = new MySqlCommand($"SELECT COALESCE(MAX({(audit ? "BitacoraId" : "AsistenciaId")}),0) FROM {table}", connection, transaction);
        long ceiling = snapshot ?? Convert.ToInt64(max.ExecuteScalar());
        string source = audit
            ? "Bitacora b LEFT JOIN Usuarios u ON u.UsuarioId=b.UsuarioId"
            : "Asistencias b JOIN Clientes c ON c.ClienteId=b.ClienteId";
        string filter = $"{id}<=@snapshot AND b.FechaHora>=@from AND b.FechaHora<@through AND " +
            (audit ? "(@search='' OR u.Nombre LIKE @pattern OR b.Accion LIKE @pattern OR b.Entidad LIKE @pattern)"
                   : "(@client IS NULL OR b.ClienteId=@client)");
        MySqlCommand Command(string sql)
        {
            var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@snapshot", ceiling);
            command.Parameters.AddWithValue("@from", from.Date);
            command.Parameters.AddWithValue("@through", through.Date.AddDays(1));
            command.Parameters.AddWithValue("@search", search.Trim());
            command.Parameters.AddWithValue("@pattern", "%" + search.Trim() + "%");
            command.Parameters.AddWithValue("@client", (object?)client ?? DBNull.Value);
            return command;
        }
        using var count = Command($"SELECT COUNT(*) FROM {source} WHERE {filter}");
        long total = Convert.ToInt64(count.ExecuteScalar());
        page = Math.Clamp(page, 0, Math.Max(0, (total - 1) / PageSize));
        string columns = audit
            ? "b.BitacoraId,COALESCE(u.Nombre,'Sistema') AS Usuario,b.Accion,b.Entidad,b.EntidadId AS Registro,b.FechaHora"
            : "b.AsistenciaId,c.NombreCompleto AS Cliente,b.FechaHora";
        using var rows = Command($"SELECT {columns} FROM {source} WHERE {filter} ORDER BY b.FechaHora DESC,{id} DESC LIMIT @size OFFSET @offset");
        rows.Parameters.AddWithValue("@size", PageSize);
        rows.Parameters.AddWithValue("@offset", page * PageSize);
        var data = ModuleUi.Read(rows);
        transaction.Commit();
        return new HistoryPage(data, total, page, ceiling);
    }
}

internal sealed class HistoryNavigation : FlowLayoutPanel
{
    private readonly Button previous = ModuleUi.Button("Anterior", false);
    private readonly Button next = ModuleUi.Button("Siguiente", false);
    private readonly Label caption = ModuleUi.Caption("Pulsa Consultar para cargar el historial.");
    public long Number { get; private set; }
    public long? Snapshot { get; private set; }
    public event Action<long>? Navigate;

    public HistoryNavigation()
    {
        Dock = DockStyle.Bottom; AutoSize = true; WrapContents = true;
        Controls.Add(previous); Controls.Add(next); Controls.Add(caption);
        previous.Enabled = next.Enabled = false;
        previous.Click += (_, _) => Navigate?.Invoke(Number - 1);
        next.Click += (_, _) => Navigate?.Invoke(Number + 1);
    }
    public void Reset()
    {
        Snapshot = null; Number = 0; previous.Enabled = next.Enabled = false;
        caption.Text = "Pulsa Consultar para actualizar los filtros.";
    }
    public void Display(HistoryPage page)
    {
        Number = page.Number; Snapshot = page.Snapshot;
        previous.Enabled = Number > 0; next.Enabled = Number + 1 < page.Pages;
        caption.Text = $"Página {Number + 1:N0} de {page.Pages:N0} · {page.Total:N0} registros · {HistoryPages.PageSize} por página";
    }
}

