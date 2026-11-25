using MySqlConnector;

namespace IntegraGym;

internal static class GymOperations
{
    public static int SaveClient(int? id, string name, string email, string phone)
    {
        AppSession.Require("clientes.gestionar");
        name = name.Trim(); email = email.Trim(); phone = phone.Trim();
        if (name.Length < 2 || name.Length > 120) throw new InvalidOperationException("El nombre debe tener entre 2 y 120 caracteres.");
        if (email.Length > 254 || (email.Length > 0 && (!System.Net.Mail.MailAddress.TryCreate(email, out var address) || address.Address != email || !address.Host.Contains('.'))))
            throw new InvalidOperationException("Escribe un correo válido o deja el campo vacío.");
        if (phone.Length > 30) throw new InvalidOperationException("El teléfono no puede superar 30 caracteres.");
        using var connection = SecurityService.Open();
        using var transaction = connection.BeginTransaction();
        if (id is int existing)
        {
            using var check = new MySqlCommand("SELECT Estado='Activo' AND DatosPurgados=FALSE FROM Clientes WHERE ClienteId=@id FOR UPDATE", connection, transaction);
            check.Parameters.AddWithValue("@id", existing);
            if (!Convert.ToBoolean(check.ExecuteScalar())) throw new InvalidOperationException("El cliente está inactivo, depurado o ya no existe. Actualiza la lista.");
        }
        using var command = new MySqlCommand(id is null
            ? "INSERT INTO Clientes(NombreCompleto,Correo,Telefono) VALUES(@name,@email,@phone)"
            : "UPDATE Clientes SET NombreCompleto=@name,Correo=@email,Telefono=@phone,FechaActualizacion=CURRENT_TIMESTAMP WHERE ClienteId=@id", connection, transaction);
        command.Parameters.AddWithValue("@id", (object?)id ?? DBNull.Value);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@email", email.Length == 0 ? DBNull.Value : email);
        command.Parameters.AddWithValue("@phone", phone.Length == 0 ? DBNull.Value : phone);
        try { command.ExecuteNonQuery(); }
        catch (MySqlException ex) when (ex.Number == 1062) { throw new InvalidOperationException("Ese correo ya está registrado para otro cliente.", ex); }
        int saved = id ?? checked((int)command.LastInsertedId);
        AuditLog.Write(connection, transaction, id is null ? "Registrar cliente" : "Modificar cliente", "Clientes", saved);
        transaction.Commit(); return saved;
    }

    public static int AssignMembership(int clientId, int planId, DateTime start, decimal price)
    {
        AppSession.Require("membresias.gestionar");
        if (price < 0 || price > 1000000 || decimal.Round(price, 2) != price)
            throw new InvalidOperationException("Revisa el precio acordado de la membresía.");
        using var connection = SecurityService.Open();
        using var transaction = connection.BeginTransaction();
        using var client = new MySqlCommand("SELECT Estado='Activo' AND DatosPurgados=FALSE FROM Clientes WHERE ClienteId=@id FOR UPDATE", connection, transaction);
        client.Parameters.AddWithValue("@id", clientId);
        if (!Convert.ToBoolean(client.ExecuteScalar())) throw new InvalidOperationException("El cliente ya no está activo. Actualiza los clientes y planes.");
        using var plan = new MySqlCommand("SELECT DuracionDias FROM Planes WHERE PlanId=@id AND Activo=TRUE FOR UPDATE", connection, transaction);
        plan.Parameters.AddWithValue("@id", planId);
        int days = Convert.ToInt32(plan.ExecuteScalar());
        if (days < 1) throw new InvalidOperationException("El plan ya no está activo. Actualiza los clientes y planes.");
        DateTime end;
        try { end = start.Date.AddDays(days - 1); }
        catch (ArgumentOutOfRangeException) { throw new InvalidOperationException("La fecha de vencimiento queda fuera del intervalo permitido."); }
        if (start.Year < 1000) throw new InvalidOperationException("Revisa la fecha de inicio.");
        using var command = new MySqlCommand("INSERT INTO Membresias(ClienteId,PlanId,FechaInicio,FechaVencimiento,PrecioAcordado,Estado,EstadoPago) VALUES(@client,@plan,@start,@end,@price,IF(@end<CURDATE(),'Vencida','Activa'),@paid)", connection, transaction);
        command.Parameters.AddWithValue("@client", clientId); command.Parameters.AddWithValue("@plan", planId);
        command.Parameters.AddWithValue("@start", start.Date); command.Parameters.AddWithValue("@end", end);
        command.Parameters.AddWithValue("@price", price); command.Parameters.AddWithValue("@paid", price == 0 ? "Pagada" : "Pendiente");
        command.ExecuteNonQuery(); int saved = checked((int)command.LastInsertedId);
        AuditLog.Write(connection, transaction, "Asignar membresía", "Membresias", saved);
        transaction.Commit(); return saved;
    }

    public static long RegisterAttendance(int clientId)
    {
        AppSession.Require("asistencias.gestionar");
        using var connection = new MySqlConnection(DatabaseSettings.ConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var client = new MySqlCommand("SELECT Estado,DatosPurgados FROM Clientes WHERE ClienteId=@id FOR UPDATE", connection, transaction);
        client.Parameters.AddWithValue("@id", clientId);
        using (var reader = client.ExecuteReader())
        {
            if (!reader.Read() || reader.GetString(0) != "Activo" || reader.GetBoolean(1))
                throw new InvalidOperationException("El cliente está inactivo o ya no tiene datos disponibles.");
        }
        using var membership = new MySqlCommand("SELECT COUNT(*) FROM Membresias WHERE ClienteId=@id AND Estado='Activa' AND FechaInicio<=CURDATE() AND FechaVencimiento>=CURDATE()", connection, transaction);
        membership.Parameters.AddWithValue("@id", clientId);
        if (Convert.ToInt32(membership.ExecuteScalar()) == 0)
            throw new InvalidOperationException("El cliente no tiene una membresía vigente. Revisa o renueva su membresía antes de registrar la entrada.");
        using var duplicate = new MySqlCommand("SELECT COUNT(*) FROM Asistencias WHERE ClienteId=@id AND FechaHora>=CURDATE() AND FechaHora<CURDATE()+INTERVAL 1 DAY", connection, transaction);
        duplicate.Parameters.AddWithValue("@id", clientId);
        if (Convert.ToInt32(duplicate.ExecuteScalar()) > 0)
            throw new InvalidOperationException("Este cliente ya tiene una entrada registrada hoy.");
        using var insert = new MySqlCommand("INSERT INTO Asistencias(ClienteId,FechaHora) VALUES(@id,CURRENT_TIMESTAMP)", connection, transaction);
        insert.Parameters.AddWithValue("@id", clientId);
        insert.ExecuteNonQuery();
        long id = insert.LastInsertedId;
        AuditLog.Write(connection, transaction, "Registrar entrada", "Asistencias", id);
        transaction.Commit();
        return id;
    }

    public static long RegisterPayment(int clientId, int? membershipId, int methodId, decimal amount, DateTime date)
    {
        AppSession.Require("pagos.gestionar");
        if (amount <= 0 || amount > 9999999999.99m || decimal.Round(amount, 2) != amount)
            throw new InvalidOperationException("El importe debe ser positivo y tener como máximo dos decimales.");
        using var connection = new MySqlConnection(DatabaseSettings.ConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var client = new MySqlCommand("SELECT Estado FROM Clientes WHERE ClienteId=@id FOR UPDATE", connection, transaction);
        client.Parameters.AddWithValue("@id", clientId);
        if (Convert.ToString(client.ExecuteScalar()) != "Activo")
            throw new InvalidOperationException("El cliente no existe o está inactivo.");
        using var method = new MySqlCommand("SELECT COUNT(*) FROM MetodosPago WHERE MetodoPagoId=@id AND Activo=TRUE", connection, transaction);
        method.Parameters.AddWithValue("@id", methodId);
        if (Convert.ToInt32(method.ExecuteScalar()) == 0)
            throw new InvalidOperationException("El método de pago no está disponible.");

        decimal remaining = 0;
        if (membershipId is int mid)
        {
            using var membership = new MySqlCommand("SELECT ClienteId,Estado,PrecioAcordado FROM Membresias WHERE MembresiaId=@id FOR UPDATE", connection, transaction);
            membership.Parameters.AddWithValue("@id", mid);
            decimal price;
            using (var reader = membership.ExecuteReader())
            {
                if (!reader.Read() || reader.GetInt32(0) != clientId || reader.GetString(1) == "Cancelada")
                    throw new InvalidOperationException("La membresía no corresponde al cliente o está cancelada.");
                price = reader.GetDecimal(2);
            }
            using var paid = new MySqlCommand("SELECT COALESCE(SUM(Importe),0) FROM Pagos WHERE MembresiaId=@id AND Estado='Aplicado'", connection, transaction);
            paid.Parameters.AddWithValue("@id", mid);
            remaining = Math.Max(price - Convert.ToDecimal(paid.ExecuteScalar()), 0);
            if (amount > remaining) throw new InvalidOperationException("El importe supera el saldo pendiente de la membresía.");
        }
        using var payment = new MySqlCommand("INSERT INTO Pagos(ClienteId,MembresiaId,MetodoPagoId,Importe,FechaPago) VALUES(@cliente,@membresia,@metodo,@importe,@fecha)", connection, transaction);
        payment.Parameters.AddWithValue("@cliente", clientId);
        payment.Parameters.AddWithValue("@membresia", (object?)membershipId ?? DBNull.Value);
        payment.Parameters.AddWithValue("@metodo", methodId);
        payment.Parameters.AddWithValue("@importe", amount);
        payment.Parameters.AddWithValue("@fecha", date);
        payment.ExecuteNonQuery();
        long paymentId = payment.LastInsertedId;
        if (membershipId is int membershipToUpdate)
        {
            using var update = new MySqlCommand("UPDATE Membresias SET EstadoPago=@estado WHERE MembresiaId=@id", connection, transaction);
            update.Parameters.AddWithValue("@estado", amount == remaining ? "Pagada" : "Parcial");
            update.Parameters.AddWithValue("@id", membershipToUpdate);
            update.ExecuteNonQuery();
        }
        AuditLog.Write(connection, transaction, "Registrar pago", "Pagos", paymentId);
        transaction.Commit();
        return paymentId;
    }
}

