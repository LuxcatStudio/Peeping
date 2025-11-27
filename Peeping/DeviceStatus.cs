using System.Text.Json.Serialization;

namespace ActivityMonitor.Models
{
    public class DeviceStatusRequest
    {
        [JsonPropertyName("globalConnectionStatus")]
        public string GlobalConnectionStatus { get; set; } = "online";

        [JsonPropertyName("receivedAt")]
        public string ReceivedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        [JsonPropertyName("devices")]
        public Devices Devices { get; set; } = new();
    }

    public class Devices
    {
        [JsonPropertyName("pc")]
        public PCDevice Pc { get; set; } = new();
    }

    public class PCDevice
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "on";

        [JsonPropertyName("software")]
        public string Software { get; set; } = string.Empty;

        [JsonPropertyName("cpuUsage")]
        public double CpuUsage { get; set; }

        [JsonPropertyName("connectionStatus")]
        public string ConnectionStatus { get; set; } = "online";
    }
}