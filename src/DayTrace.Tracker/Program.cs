namespace DayTrace.Tracker;

internal static class Program
{
    // M1 scaffold only: the tray ApplicationContext, monitors and recorder wiring arrive in M2/M3.
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
    }
}
