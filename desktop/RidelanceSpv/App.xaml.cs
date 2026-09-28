using System.Windows;

namespace RidelanceSpv;

/// <summary>O singură instanță: două ferestre ar încerca două trimiteri în paralel.</summary>
#pragma warning disable CA1001 // Mutex-ul trăiește cât aplicația și se eliberează în OnExit.
public partial class App : Application
#pragma warning restore CA1001
{
    private Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        _single = new Mutex(initiallyOwned: true, @"Local\RIDElanceSPV", out bool first);
        if (!first)
        {
            MessageBox.Show("RIDElance SPV e deja deschis.", "RIDElance SPV", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }
}
