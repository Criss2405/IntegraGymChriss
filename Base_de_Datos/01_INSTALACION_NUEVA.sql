-- INTEGRA GYM | INSTALACIÓN INICIAL SIN DATOS DE CLIENTES
-- Ejecuta este archivo UNA SOLA VEZ en una instalación nueva de MySQL Server.
-- Crea el esquema integra_gym, las 12 tablas, función y procedimientos, catálogo de roles,
-- permisos iniciales, métodos de pago y el evento de retención de clientes inactivos.
-- NO crea planes, clientes, membresías, pagos, asistencias ni usuarios. La primera cuenta
-- Administrador se crea desde la pantalla de inicio de sesión de la aplicación.
-- Requiere MySQL Server 8.0.16+ y permisos administrativos para crear esquema/evento,
-- y para habilitar event_scheduler mediante SET PERSIST.
-- No ejecutes sobre una base que ya contenga información.
-- Integra Gym: instalación inicial para una computadora nueva.
-- MySQL Server 8.0.16 o superior. Ejecutar una sola vez en una instancia local vacía.
-- Crea tablas, roles, permisos, métodos de pago y mantenimiento; no inserta clientes, pagos ni usuarios.
CREATE DATABASE IF NOT EXISTS integra_gym CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE integra_gym;

CREATE TABLE IF NOT EXISTS Roles (RolId INT AUTO_INCREMENT PRIMARY KEY, Nombre VARCHAR(40) NOT NULL UNIQUE, Activo BOOLEAN NOT NULL DEFAULT TRUE) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS Usuarios (UsuarioId INT AUTO_INCREMENT PRIMARY KEY, RolId INT NOT NULL, Nombre VARCHAR(100) NOT NULL, Correo VARCHAR(254) NOT NULL UNIQUE, HashContrasena VARCHAR(500) NOT NULL, Activo BOOLEAN NOT NULL DEFAULT TRUE, FechaAlta DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY (RolId) REFERENCES Roles(RolId)) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS Permisos (PermisoId INT AUTO_INCREMENT PRIMARY KEY, Codigo VARCHAR(80) NOT NULL UNIQUE, Descripcion VARCHAR(200) NOT NULL) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS RolesPermisos (RolId INT NOT NULL, PermisoId INT NOT NULL, PRIMARY KEY(RolId,PermisoId), FOREIGN KEY(RolId) REFERENCES Roles(RolId), FOREIGN KEY(PermisoId) REFERENCES Permisos(PermisoId)) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS Clientes (ClienteId INT AUTO_INCREMENT PRIMARY KEY, NombreCompleto VARCHAR(120) NOT NULL, Correo VARCHAR(254) NULL UNIQUE, Telefono VARCHAR(30) NULL, Estado ENUM('Activo','Inactivo') NOT NULL DEFAULT 'Activo', FechaRegistro DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, FechaActualizacion DATETIME NULL DEFAULT NULL, FechaInactivacion DATETIME NULL, DatosPurgados BOOLEAN NOT NULL DEFAULT FALSE, INDEX IX_Clientes_Estado_FechaInactivacion(Estado,FechaInactivacion)) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS Entrenadores (EntrenadorId INT AUTO_INCREMENT PRIMARY KEY, NombreCompleto VARCHAR(120) NOT NULL, Correo VARCHAR(254) NULL, Telefono VARCHAR(30) NULL, Estado ENUM('Activo','Inactivo') NOT NULL DEFAULT 'Activo', FechaAlta DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS Planes (PlanId INT AUTO_INCREMENT PRIMARY KEY, Nombre VARCHAR(80) NOT NULL UNIQUE, DuracionDias INT NOT NULL, Precio DECIMAL(12,2) NOT NULL, Activo BOOLEAN NOT NULL DEFAULT TRUE, CHECK(DuracionDias>0), CHECK(Precio>=0)) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS Membresias (MembresiaId INT AUTO_INCREMENT PRIMARY KEY, ClienteId INT NOT NULL, PlanId INT NOT NULL, FechaInicio DATE NOT NULL, FechaVencimiento DATE NOT NULL, PrecioAcordado DECIMAL(12,2) NOT NULL, Estado ENUM('Activa','Vencida','Cancelada') NOT NULL DEFAULT 'Activa', EstadoPago ENUM('Pendiente','Parcial','Pagada') NOT NULL DEFAULT 'Pendiente', FOREIGN KEY(ClienteId) REFERENCES Clientes(ClienteId), FOREIGN KEY(PlanId) REFERENCES Planes(PlanId), CHECK(FechaVencimiento>=FechaInicio), CHECK(PrecioAcordado>=0)) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS MetodosPago (MetodoPagoId INT AUTO_INCREMENT PRIMARY KEY, Nombre VARCHAR(40) NOT NULL UNIQUE, Activo BOOLEAN NOT NULL DEFAULT TRUE) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS Pagos (PagoId BIGINT AUTO_INCREMENT PRIMARY KEY, ClienteId INT NOT NULL, MembresiaId INT NULL, MetodoPagoId INT NOT NULL, Importe DECIMAL(12,2) NOT NULL, FechaPago DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, Estado ENUM('Aplicado','Anulado') NOT NULL DEFAULT 'Aplicado', FOREIGN KEY(ClienteId) REFERENCES Clientes(ClienteId), FOREIGN KEY(MembresiaId) REFERENCES Membresias(MembresiaId), FOREIGN KEY(MetodoPagoId) REFERENCES MetodosPago(MetodoPagoId), CHECK(Importe>0), INDEX IX_Pagos_Membresia(MembresiaId)) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS Asistencias (AsistenciaId BIGINT AUTO_INCREMENT PRIMARY KEY, ClienteId INT NOT NULL, FechaHora DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY(ClienteId) REFERENCES Clientes(ClienteId)) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS Bitacora (BitacoraId BIGINT AUTO_INCREMENT PRIMARY KEY, UsuarioId INT NULL, Accion VARCHAR(100) NOT NULL, Entidad VARCHAR(80) NOT NULL, EntidadId INT NULL, FechaHora DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY(UsuarioId) REFERENCES Usuarios(UsuarioId)) ENGINE=InnoDB;

INSERT IGNORE INTO Roles(Nombre) VALUES ('Administrador'),('Recepción'),('Entrenador');
INSERT IGNORE INTO MetodosPago(Nombre) VALUES ('Efectivo'),('Tarjeta'),('Transferencia');
INSERT IGNORE INTO Permisos(Codigo,Descripcion) VALUES
('clientes.gestionar','Consultar el directorio, registrar clientes, actualizar datos y dar de baja'),
('planes.gestionar','Consultar el catálogo, crear planes, editar datos y desactivarlos'),
('membresias.gestionar','Asignar, consultar, renovar y cancelar membresías'),
('pagos.gestionar','Registrar cobros y consultar el historial de pagos'),
('entrenadores.gestionar','Registrar, editar y activar o dar de baja entrenadores'),
('reportes.ver','Consultar y exportar reportes'),
('usuarios.gestionar','Crear y editar cuentas, asignar roles y configurar permisos');
INSERT IGNORE INTO RolesPermisos(RolId,PermisoId)
SELECT r.RolId,p.PermisoId FROM Roles r JOIN Permisos p
WHERE (r.Nombre='Administrador')
   OR (r.Nombre='Recepción' AND p.Codigo IN ('clientes.gestionar','membresias.gestionar','pagos.gestionar','reportes.ver'))
   OR (r.Nombre='Entrenador' AND p.Codigo IN ('clientes.gestionar','membresias.gestionar'));

DELIMITER $$
CREATE FUNCTION fn_SaldoMembresia(p_MembresiaId INT) RETURNS DECIMAL(12,2)
READS SQL DATA
BEGIN
  DECLARE v_Precio DECIMAL(12,2);
  DECLARE v_Pagado DECIMAL(12,2) DEFAULT 0;
  SELECT PrecioAcordado INTO v_Precio FROM Membresias WHERE MembresiaId=p_MembresiaId;
  IF v_Precio IS NULL THEN RETURN NULL; END IF;
  SELECT COALESCE(SUM(Importe),0) INTO v_Pagado FROM Pagos WHERE MembresiaId=p_MembresiaId AND Estado='Aplicado';
  RETURN GREATEST(v_Precio-v_Pagado,0);
END$$

CREATE PROCEDURE sp_RegistrarPago(IN p_ClienteId INT, IN p_MembresiaId INT, IN p_MetodoPagoId INT, IN p_Importe DECIMAL(12,2), IN p_FechaPago DATETIME)
BEGIN
  DECLARE v_EstadoCliente VARCHAR(20) DEFAULT NULL;
  DECLARE v_ClienteMembresia INT DEFAULT NULL;
  DECLARE v_EstadoMembresia VARCHAR(20) DEFAULT NULL;
  DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
  IF p_Importe<=0 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='El importe debe ser mayor que cero'; END IF;
  START TRANSACTION;
  SELECT Estado INTO v_EstadoCliente FROM Clientes WHERE ClienteId=p_ClienteId FOR UPDATE;
  IF v_EstadoCliente IS NULL OR v_EstadoCliente<>'Activo' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='El cliente no existe o está inactivo'; END IF;
  IF NOT EXISTS(SELECT 1 FROM MetodosPago WHERE MetodoPagoId=p_MetodoPagoId AND Activo=TRUE) THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Método de pago no disponible'; END IF;
  IF p_MembresiaId IS NOT NULL THEN
    SELECT ClienteId, Estado INTO v_ClienteMembresia, v_EstadoMembresia FROM Membresias WHERE MembresiaId=p_MembresiaId FOR UPDATE;
    IF v_ClienteMembresia IS NULL OR v_ClienteMembresia<>p_ClienteId OR v_EstadoMembresia='Cancelada' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='La membresía no corresponde al cliente o está cancelada'; END IF;
    IF p_Importe>fn_SaldoMembresia(p_MembresiaId) THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='El importe supera el saldo pendiente'; END IF;
  END IF;
  INSERT INTO Pagos(ClienteId,MembresiaId,MetodoPagoId,Importe,FechaPago) VALUES(p_ClienteId,p_MembresiaId,p_MetodoPagoId,p_Importe,COALESCE(p_FechaPago,CURRENT_TIMESTAMP));
  IF p_MembresiaId IS NOT NULL THEN
    IF fn_SaldoMembresia(p_MembresiaId)=0 THEN
      UPDATE Membresias SET EstadoPago='Pagada' WHERE MembresiaId=p_MembresiaId;
    ELSE
      UPDATE Membresias SET EstadoPago='Parcial' WHERE MembresiaId=p_MembresiaId;
    END IF;
  END IF;
  COMMIT;
END$$
DELIMITER ;

-- Después de crear la base, crea un usuario local para la aplicación.
-- Cambia CLAVE_LARGA_UNICA antes de ejecutar estas dos líneas por separado:
-- CREATE USER 'integra_app'@'localhost' IDENTIFIED BY 'CLAVE_LARGA_UNICA';
-- GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON integra_gym.* TO 'integra_app'@'localhost';

-- Mantenimiento automático de bajas de clientes.

-- Integra Gym 1.2. Ejecutar con una cuenta administradora sobre integra_gym.
-- Repetible. No elimina registros. La retención queda apagada hasta configurarla.
CREATE TABLE IF NOT EXISTS Configuracion (Id INT PRIMARY KEY, RetencionActiva BOOLEAN NOT NULL DEFAULT FALSE, DiasRetencion INT NOT NULL DEFAULT 30, CHECK(DiasRetencion BETWEEN 30 AND 3650)) ENGINE=InnoDB;
INSERT IGNORE INTO Configuracion(Id) VALUES(1);
CREATE TABLE IF NOT EXISTS SeguridadUsuarios (UsuarioId INT PRIMARY KEY, Intentos INT NOT NULL DEFAULT 0, BloqueadoHasta DATETIME NULL, CambioObligatorio BOOLEAN NOT NULL DEFAULT FALSE, RecuperacionHash VARCHAR(500) NULL, FOREIGN KEY(UsuarioId) REFERENCES Usuarios(UsuarioId)) ENGINE=InnoDB;
DROP EVENT IF EXISTS ev_PurgarClientesInactivos;
DROP PROCEDURE IF EXISTS sp_PurgarClientesInactivos;
DELIMITER $$
CREATE PROCEDURE sp_PurgarClientesInactivos()
BEGIN
  DECLARE v_Activa BOOLEAN DEFAULT FALSE;
  DECLARE v_Dias INT DEFAULT 30;
  DECLARE v_Total INT DEFAULT 0;
  DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
  START TRANSACTION;
  SELECT RetencionActiva,DiasRetencion INTO v_Activa,v_Dias FROM Configuracion WHERE Id=1 FOR UPDATE;
  IF v_Activa THEN
    UPDATE Clientes SET NombreCompleto=CONCAT('Cliente archivado #',ClienteId),Correo=NULL,Telefono=NULL,DatosPurgados=TRUE,FechaActualizacion=CURRENT_TIMESTAMP
    WHERE Estado='Inactivo' AND DatosPurgados=FALSE AND FechaInactivacion <= CURRENT_TIMESTAMP - INTERVAL v_Dias DAY;
    SET v_Total=ROW_COUNT();
    IF v_Total>0 THEN
      INSERT INTO Bitacora(Accion,Entidad) VALUES(CONCAT('Anonimizar clientes por política: ',v_Total),'Clientes');
    END IF;
  END IF;
  COMMIT;
END$$
DELIMITER ;
CREATE EVENT ev_PurgarClientesInactivos ON SCHEDULE EVERY 1 HOUR STARTS CURRENT_TIMESTAMP + INTERVAL 1 HOUR DO CALL sp_PurgarClientesInactivos();
CREATE TABLE IF NOT EXISTS Migraciones (Version INT PRIMARY KEY, Aplicada DATETIME DEFAULT CURRENT_TIMESTAMP) ENGINE=InnoDB;
INSERT IGNORE INTO Migraciones(Version) VALUES(2);

