using ActivityMonitor.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ActivityMonitor.Services
{
    public class ApiService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private bool _disposed;
        private string? _apiUrl = string.Empty;
        private readonly object _lockObj = new();

        public ApiService()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true
            };
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<bool> InitializeApiUrlAsync(string apiUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(apiUrl))
                {
                    System.Diagnostics.Debug.WriteLine("API URL为空");
                    return false;
                }

                lock (_lockObj)
                {
                    _apiUrl = apiUrl;
                }

                var testResponse = await _httpClient.GetAsync(apiUrl);

                return testResponse.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初始化API URL失败: {ex.Message}");
                return false;
            }
        }

        public void UpdateApiUrl(string apiUrl)
        {
            lock (_lockObj)
            {
                _apiUrl = apiUrl;
            }
            System.Diagnostics.Debug.WriteLine($"API URL已更新为: {apiUrl}");
        }

        public async Task<bool> SendDeviceStatusAsync(DeviceStatusRequest status)
        {
            string? currentUrl;
            lock (_lockObj)
            {
                currentUrl = _apiUrl;
            }

            if (string.IsNullOrEmpty(currentUrl))
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
                var response = await _httpClient.PostAsync(currentUrl, content);

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
                _httpClient?.Dispose();
            }

            _disposed = true;
        }

        ~ApiService()
        {
            Dispose(false);
        }
    }
}