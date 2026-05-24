using System.IO;
using System.Text.Json;

namespace ActivityMonitor.Services
{
    public class SettingsService
    {
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Peeping",
            "settings.json"
        );

        public AppSettings Settings { get; private set; } = new();

        public event EventHandler? SettingsChanged;

        public SettingsService()
        {
            LoadSettings();
        }

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    Settings = new AppSettings();
                    SaveSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载设置失败: {ex.Message}");
                Settings = new AppSettings();
            }
        }

        public void SaveSettings()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(Settings, options);
                File.WriteAllText(SettingsFilePath, json);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存设置失败: {ex.Message}");
            }
        }

        public void UpdateApiUrl(string apiUrl)
        {
            Settings.ApiUrl = apiUrl;
            SaveSettings();
        }

        public void UpdateAutoStart(bool enabled)
        {
            Settings.AutoStart = enabled;
            SaveSettings();
            ApplyAutoStart(enabled);
        }

        public void ApplyAutoStart(bool enabled)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

                if (key != null)
                {
                    var appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(appPath))
                    {
                        if (enabled)
                        {
                            key.SetValue("Peeping", $"\"{appPath}\"");
                        }
                        else
                        {
                            key.DeleteValue("Peeping", false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置开机自启动失败: {ex.Message}");
            }
        }
    }

    public class AppSettings
    {
        public string ApiUrl { get; set; } = "http://localhost:3000/api/status";
        public bool AutoStart { get; set; } = false;
    }
}