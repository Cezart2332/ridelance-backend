using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using RidelanceSpv.Core.Server;

namespace RidelanceSpv;

/// <summary>
/// Setările aplicației, în <c>%AppData%\RIDElance SPV\settings.json</c>. Cheia RIDElance e criptată
/// cu DPAPI (doar utilizatorul Windows curent o poate citi). PIN-ul stickului nu se salvează: îl
/// cere driverul stickului.
/// </summary>
internal sealed class AppSettings
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RIDElance SPV");
    private static readonly string File = Path.Combine(Folder, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "RIDElance SPV";

    public string ServerUrl { get; set; } = RidelanceClient.DefaultBaseUrl;

    /// <summary>Cheia RIDElance, criptată DPAPI, base64.</summary>
    public string? KeyProtected { get; set; }

    public string? CertificateThumbprint { get; set; }

    public int IntervalMinutes { get; set; } = 60;

    public static AppSettings Load()
    {
        try
        {
            return System.IO.File.Exists(File)
                ? JsonSerializer.Deserialize<AppSettings>(System.IO.File.ReadAllText(File)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        System.IO.File.WriteAllText(File, JsonSerializer.Serialize(this, Json));
    }

    public string? Key
    {
        get
        {
            if (string.IsNullOrEmpty(KeyProtected))
            {
                return null;
            }

            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(KeyProtected), null, DataProtectionScope.CurrentUser));
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                return null;
            }
        }

        set => KeyProtected = value is null
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    /// <summary>Pornirea odată cu Windows, pentru utilizatorul curent (fără drepturi de administrator).</summary>
    public static bool StartsWithWindows
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is not null;
        }

        set
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value && Environment.ProcessPath is { } exe)
            {
                key.SetValue(RunName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(RunName, throwOnMissingValue: false);
            }
        }
    }
}
