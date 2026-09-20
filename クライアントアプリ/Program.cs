namespace DrawingGame.Client;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        var setup = new SetupForm();
        if (setup.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        Application.Run(new MainForm(setup.ServerIp, setup.PcNumber));
    }
}
