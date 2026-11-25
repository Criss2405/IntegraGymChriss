# Integra Gym

Aplicación de escritorio para gestionar clientes, planes, membresías, pagos, asistencias, reportes, usuarios, permisos y respaldos de un gimnasio.

## Tecnología

- C# y Windows Forms (WinForms) en Windows.
- .NET 10 (`net10.0-windows`), con MySqlConnector 2.5.0.
- MySQL Server 8.0.16 o superior, según el requisito del script de instalación.

## Ejecutar en Windows

1. Instala MySQL Server 8 y .NET Desktop Runtime 10. Para compilar desde el código necesitas también el SDK de .NET 10 o Visual Studio compatible con .NET 10 y la carga de trabajo «Desarrollo de escritorio de .NET».
2. En una instancia nueva, ejecuta `Base_de_Datos/01_INSTALACION_NUEVA.sql` con una cuenta administrativa de MySQL. Para actualizar una instalación v1.1 existente, respáldala primero y usa `02_ACTUALIZAR_DESDE_v1.1.sql`.
3. Crea tu usuario de aplicación de MySQL siguiendo el ejemplo comentado al final de la sección de permisos del script inicial. Asigna tu propia contraseña.
4. Abre `IntegraGym.sln` y ejecuta el proyecto `IntegraGym`, o usa `dotnet run --project IntegraGym/IntegraGym.csproj` desde la raíz.
5. Configura el servidor, puerto, esquema, usuario y contraseña desde la pantalla de conexión. También puedes definir `INTEGRAGYM_DB_CONNECTION` en tu entorno local. Ejemplo con valores ficticios:

   ```text
   Server=127.0.0.1;Port=3306;Database=integra_gym;User=integra_app;Password=TU_CONTRASEÑA
   ```

   Sustituye los valores por los de tu entorno y guarda las credenciales fuera del repositorio. La primera cuenta administradora de Integra Gym se crea desde la aplicación.

`Base_de_Datos/002_security_settings.sql` se incorpora a la aplicación como recurso para actualizar el esquema. `Instalador/` contiene el código del instalador de Windows. La compilación pública funciona sin la clave privada de firma del proyecto original.
