using ActivityMonitor.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ActivityMonitor.Services
{
    public class SystemMonitorService : IDisposable
    {
        private readonly PerformanceCounter _cpuCounter;
        private bool _disposed;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public SystemMonitorService()
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        }

        public string GetActiveApplication()
        {
            try
            {
                IntPtr foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero)
                    return "未知应用";

                GetWindowThreadProcessId(foregroundWindow, out uint processId);

                if (processId == 0)
                    return "未知应用";

                using var process = Process.GetProcessById((int)processId);
                string appName = string.IsNullOrEmpty(process.MainWindowTitle)
                    ? process.ProcessName
                    : $"{process.ProcessName} - {process.MainWindowTitle}";
                
                return ExtractMainProcess(appName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取活动应用失败: {ex.Message}");
                return "未知应用";
            }
        }
        
        private string ExtractMainProcess(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return fullName;
            var match = Regex.Match(fullName, @"^([^\s\-]+)");
            
            if (match.Success)
            {
                return match.Value.Trim();
            }
            return fullName;
        }

        public double GetCpuUsage()
        {
            try
            {
                return Math.Round(_cpuCounter.NextValue(), 2);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取CPU使用率失败: {ex.Message}");
                return 0.0;
            }
        }

        public DeviceStatusRequest GetDeviceStatus()
        {
            return new DeviceStatusRequest
            {
                ReceivedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                Devices = new Devices
                {
                    Pc = new PCDevice
                    {
                        Software = GetActiveApplication(),
                        CpuUsage = GetCpuUsage()
                    }
                }
            };
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _cpuCounter?.Dispose();
            }

            _disposed = true;
        }

        ~SystemMonitorService()
        {
            Dispose(false);
        }
    }
}
