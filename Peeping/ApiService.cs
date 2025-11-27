using ActivityMonitor.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ActivityMonitor.Services
{
    public class ApiService
    {
        private readonly HttpClient _httpClient;
        private string _apiUrl = string.Empty;

        public ApiService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<bool> InitializeApiUrlAsync()
        {
            try
            {
                _apiUrl = "http://localhost:3000/api/status";

                var testResponse = await _httpClient.GetAsync(_apiUrl);
                return testResponse.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初始化API URL失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendDeviceStatusAsync(DeviceStatusRequest status)
        {
            if (string.IsNullOrEmpty(_apiUrl))
            {
                System.Diagnostics.Debug.WriteLine("API URL未初始化");
                return false;
            }

            try
            {
                var json = JsonSerializer.Serialize(status, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(_apiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"状态发送成功: {DateTime.Now}");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"API响应失败: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"发送状态失败: {ex.Message}");
                return false;
            }
        }
    }
}