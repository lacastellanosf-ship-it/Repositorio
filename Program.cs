namespace Citaspp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Citaspp",
            "citas.db");
        var repository = new AppointmentRepository(databasePath);
        repository.Initialize();
        Application.Run(new MainForm(repository));
    }
}
