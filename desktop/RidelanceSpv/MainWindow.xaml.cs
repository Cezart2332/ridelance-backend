using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using RidelanceSpv.Core.Anaf;
using RidelanceSpv.Core.Server;
using RidelanceSpv.Core.Sync;

namespace RidelanceSpv;

/// <summary>
/// Singurul ecran: ce cont, câți clienți, ce certificat, când a trimis și când trimite din nou.
/// Trimite la pornire, la ieșirea din sleep și apoi la intervalul ales. Tot restul se face în web.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly CultureInfo Ro = new("ro-RO");
    private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WaitAfterResume = TimeSpan.FromSeconds(30);

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ObservableCollection<string> _log = [];
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime? _nextRunAt;
    private bool _running;
    private bool _loading = true;

    public MainWindow()
    {
        InitializeComponent();
        LogList.ItemsSource = _log;
        _clock.Tick += (_, _) => Tick();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Loaded += async (_, _) => await ShowStateAsync();
        Closed += (_, _) =>
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _clock.Stop();
        };
    }

    private static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    private async Task ShowStateAsync()
    {
        ServerBox.Text = _settings.ServerUrl;
        List<RadioButton> intervals = [.. IntervalPanel.Children.OfType<RadioButton>()];
        (intervals.FirstOrDefault(i => (string)i.Tag == _settings.IntervalMinutes.ToString(CultureInfo.InvariantCulture)) ?? intervals[1]).IsChecked = true;
        StartupBox.IsChecked = AppSettings.StartsWithWindows;
        _loading = false;

        bool connected = _settings.Key is not null;
        ConnectCard.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        MainPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        if (!connected)
        {
            return;
        }

        ShowCertificate();
        await RefreshStatusAsync();
        // Trimite imediat: după o pauză lungă (PC închis) serverul cere exact zilele lipsă.
        _nextRunAt = DateTime.Now;
        _clock.Start();
    }

    private async void OnConnect(object sender, RoutedEventArgs e)
    {
        ConnectError.Visibility = Visibility.Collapsed;
        string key = KeyBox.Password.Trim();
        string server = ServerBox.Text.Trim();
        ConnectButton.IsEnabled = false;
        try
        {
            using var client = new RidelanceClient(server, key);
            await client.StatusAsync(CancellationToken.None);
            _settings.ServerUrl = server;
            _settings.Key = key;
            _settings.Save();
            KeyBox.Clear();
            await ShowStateAsync();
        }
        catch (Exception exception) when (exception is RidelanceException or UriFormatException)
        {
            ConnectError.Text = exception is RidelanceException ? exception.Message : "Adresa serverului nu e validă.";
            ConnectError.Visibility = Visibility.Visible;
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private async void OnDisconnect(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Aplicația nu mai trimite date până la o nouă conectare.", "Deconectează", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        _clock.Stop();
        _nextRunAt = null;
        _settings.Key = null;
        _settings.Save();
        _log.Clear();
        await ShowStateAsync();
    }

    private void OnChooseCertificate(object sender, RoutedEventArgs e)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var usable = new X509Certificate2Collection();
        foreach (X509Certificate2 certificate in store.Certificates)
        {
            if (certificate.HasPrivateKey && certificate.NotAfter > DateTime.Now)
            {
                usable.Add(certificate);
            }
        }

        if (usable.Count == 0)
        {
            MessageBox.Show("Nu găsesc niciun certificat. Conectează stickul și încearcă din nou.", "Certificat", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        X509Certificate2Collection chosen = X509Certificate2UI.SelectFromCollection(
            usable, "Certificat SPV", "Alege certificatul cu care intri în SPV.", X509SelectionFlag.SingleSelection, new System.Windows.Interop.WindowInteropHelper(this).Handle);
        if (chosen.Count == 1)
        {
            _settings.CertificateThumbprint = chosen[0].Thumbprint;
            _settings.Save();
            ShowCertificate();
            _nextRunAt ??= DateTime.Now;
        }
    }

    private async void OnSendNow(object sender, RoutedEventArgs e) => await SyncAsync();

    private void OnIntervalChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        _settings.IntervalMinutes = int.Parse(tag, CultureInfo.InvariantCulture);
        _settings.Save();
        if (_nextRunAt is { } next && next > DateTime.Now.AddMinutes(_settings.IntervalMinutes))
        {
            _nextRunAt = DateTime.Now.AddMinutes(_settings.IntervalMinutes);
        }
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            AppSettings.StartsWithWindows = StartupBox.IsChecked == true;
        }
    }

    /// <summary>La ieșirea din sleep sau hibernare: trimite curând, după ce revine rețeaua.</summary>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Dispatcher.Invoke(() =>
            {
                if (_settings.Key is not null)
                {
                    _nextRunAt = DateTime.Now + WaitAfterResume;
                }
            });
        }
    }

    private async void Tick()
    {
        if (_nextRunAt is not { } next)
        {
            NextText.Text = "—";
            return;
        }

        TimeSpan left = next - DateTime.Now;
        NextText.Text = _running ? "acum" : left <= TimeSpan.Zero ? "acum" : $"în {(int)left.TotalMinutes:00}:{left.Seconds:00}";
        if (left <= TimeSpan.Zero && !_running)
        {
            await SyncAsync();
        }
    }

    private async Task SyncAsync()
    {
        if (_running || _settings.Key is not { } key)
        {
            return;
        }

        X509Certificate2? certificate = FindCertificate();
        if (certificate is null)
        {
            Log(_settings.CertificateThumbprint is null ? "Alege certificatul." : "Conectează stickul.");
            _nextRunAt = DateTime.Now + RetryAfterError;
            ShowCertificate();
            return;
        }

        _running = true;
        SendButton.IsEnabled = false;
        try
        {
            using var spv = new SpvClient(certificate);
            using var server = new RidelanceClient(_settings.ServerUrl, key);
            var engine = new SyncEngine(spv, server, Environment.MachineName, Version);
            SyncReport report = await engine.RunAsync(new Progress<string>(Log), CancellationToken.None);
            _nextRunAt = DateTime.Now + (report.Succeeded || report.Skipped ? TimeSpan.FromMinutes(_settings.IntervalMinutes) : RetryAfterError);
            await RefreshStatusAsync();
        }
        catch (RidelanceException exception)
        {
            Log(exception.Message);
            _nextRunAt = DateTime.Now + RetryAfterError;
        }
        finally
        {
            certificate.Dispose();
            _running = false;
            SendButton.IsEnabled = true;
        }
    }

    private async Task RefreshStatusAsync()
    {
        if (_settings.Key is not { } key)
        {
            return;
        }

        try
        {
            using var server = new RidelanceClient(_settings.ServerUrl, key);
            SpvAgentStatus status = await server.StatusAsync(CancellationToken.None);
            AccountText.Text = status.KeyName;
            PfasText.Text = status.Pfas.ToString(Ro);
            QueuedText.Text = status.QueuedRequests.ToString(Ro);
            LastText.Text = status.LastSuccessAtUtc is { } last ? last.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Ro) : "niciuna";
        }
        catch (RidelanceException exception)
        {
            Log(exception.Message);
        }
    }

    private void ShowCertificate()
    {
        using X509Certificate2? certificate = FindCertificate();
        CertificateText.Text = certificate is null
            ? _settings.CertificateThumbprint is null ? "neales" : "stick neconectat"
            : $"{certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false)}, {certificate.NotAfter.ToString("dd.MM.yyyy", Ro)}";
    }

    /// <summary>Certificatul ales, dacă stickul e conectat (Windows îl arată în magazinul utilizatorului doar atunci).</summary>
    private X509Certificate2? FindCertificate()
    {
        if (_settings.CertificateThumbprint is not { } thumbprint)
        {
            return null;
        }

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        X509Certificate2Collection found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        return found.Count > 0 && found[0].HasPrivateKey ? found[0] : null;
    }

    private void Log(string line)
    {
        _log.Insert(0, $"{DateTime.Now.ToString("HH:mm", Ro)}  {line}");
        while (_log.Count > 50)
        {
            _log.RemoveAt(_log.Count - 1);
        }
    }
}
