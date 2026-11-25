using System.Windows.Forms;

namespace IntegraGym;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "No se completó la operación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (args.Contains("--scheduled-backup")) { Environment.ExitCode = ScheduledBackup.RunAsync().GetAwaiter().GetResult(); return; }
        if (string.IsNullOrWhiteSpace(DatabaseSettings.ConnectionString)) { using var setup = new SetupForm(); if (setup.ShowDialog() != DialogResult.OK) return; }
        while (true)
        {
            using var login = new LoginForm();
            if (login.ShowDialog() != DialogResult.OK || login.AuthenticatedUser is not AppUser user)
                return;

            AppSession.Start(user);
            try
            {
                if (SecurityService.MustChange(user.Id)) { using var change = new PasswordForm(); if (change.ShowDialog() != DialogResult.OK) continue; }
                using var main = new MainForm(user);
                Application.Run(main);
                try { AuditLog.Record(main.LogoutRequested ? "Cerrar sesión" : "Cerrar aplicación", "Usuarios", user.Id); }
                catch (MySqlConnector.MySqlException)
                {
                    MessageBox.Show("La sesión terminó, pero no se pudo guardar el cierre en la bitácora porque MySQL no está disponible.", "Bitácora", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                if (!main.LogoutRequested) return;
            }
            finally { AppSession.Clear(); }
        }
    }
}


