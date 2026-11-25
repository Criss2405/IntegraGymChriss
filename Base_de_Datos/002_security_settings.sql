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
