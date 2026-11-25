using MySqlConnector;

namespace IntegraGym;

internal static class AppSession
{
    public static AppUser? CurrentUser { get; private set; }
    public static void Start(AppUser user) => CurrentUser = user;
    public static void Clear() => CurrentUser = null;
    public static void Require(string permission)
    {
        if (CurrentUser is null || !CurrentUser.Can(permission))
            throw new UnauthorizedAccessException("Tu cuenta no tiene permiso para realizar esta acción.");
        using var c = SecurityService.Open();
        using var q = new MySqlCommand("SELECT COUNT(*) FROM Usuarios u JOIN Roles r ON r.RolId=u.RolId JOIN RolesPermisos rp ON rp.RolId=r.RolId JOIN Permisos p ON p.PermisoId=rp.PermisoId WHERE u.UsuarioId=@id AND u.Activo=TRUE AND r.Activo=TRUE AND p.Codigo=@code", c);
        q.Parameters.AddWithValue("@id", CurrentUser.Id); q.Parameters.AddWithValue("@code", permission);
        if (Convert.ToInt32(q.ExecuteScalar()) == 0) throw new UnauthorizedAccessException("Tu acceso cambió. Cierra sesión y vuelve a ingresar.");
    }
}

internal static class AuditLog
{
    public static void Write(MySqlConnection connection, MySqlTransaction? transaction, string action,
        string entity, long? entityId = null, int? actorId = null)
    {
        using var command = new MySqlCommand("INSERT INTO Bitacora(UsuarioId,Accion,Entidad,EntidadId) VALUES(@usuario,@accion,@entidad,@registro)", connection, transaction);
        command.Parameters.AddWithValue("@usuario", (object?)actorId ?? (object?)AppSession.CurrentUser?.Id ?? DBNull.Value);
        command.Parameters.AddWithValue("@accion", action);
        command.Parameters.AddWithValue("@entidad", entity);
        // The existing schema uses INT for EntidadId. Preserve very large IDs in the action text.
        if (entityId is > int.MaxValue or < int.MinValue)
        {
            command.Parameters["@accion"].Value = $"{action} #{entityId}";
            entityId = null;
        }
        command.Parameters.AddWithValue("@registro", (object?)entityId ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public static void Record(string action, string entity, long? entityId = null, int? actorId = null)
    {
        using var connection = new MySqlConnection(DatabaseSettings.ConnectionString);
        connection.Open();
        Write(connection, null, action, entity, entityId, actorId);
    }

    public static int Execute(MySqlConnection connection, MySqlCommand command, string action, string entity, long? entityId = null)
    {
        string? permission = entity switch
        {
            "Clientes" => "clientes.gestionar", "Planes" => "planes.gestionar",
            "Membresias" => "membresias.gestionar", "Entrenadores" => "entrenadores.gestionar",
            "Usuarios" => "usuarios.gestionar", _ => null
        };
        if (permission is not null) AppSession.Require(permission);
        if (entity == "Usuarios") SecurityService.RequireAdmin();
        using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        int affected = command.ExecuteNonQuery();
        if (affected > 0)
            Write(connection, transaction, action, entity, entityId ?? (command.LastInsertedId > 0 ? command.LastInsertedId : null));
        transaction.Commit();
        return affected;
    }
}

