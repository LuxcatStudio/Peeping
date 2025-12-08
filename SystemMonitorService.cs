using ActivityMonitor.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ActivityMonitor.Services
{
    public class SystemMonitorService
    {
        private readonly PerformanceCounter _cpuCounter;

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

                Process process = Process.GetProcessById((int)processId);
                string appName = string.IsNullOrEmpty(process.MainWindowTitle)
                    ? process.ProcessName
                    : $"{process.ProcessName} - {process.MainWindowTitle}";
                
                return ExtractMainProcess(appName);
            }
            catch
            {
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
            catch
            {
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
            _cpuCounter?.Dispose();
        }
    }
}
