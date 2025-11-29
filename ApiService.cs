using ActivityMonitor.Models;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace ActivityMonitor.Services
{
    public class ApiService
    {
        private readonly HttpClient _httpClient;
        private string? _apiUrl = string.Empty;

        public ApiService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }


        public async Task<bool> InitializeApiUrlAsync()
        {
            try
            {
                _apiUrl = GetApiUrlFromConfig();

                if (string.IsNullOrEmpty(_apiUrl))
                {
                    System.Diagnostics.Debug.WriteLine("未能从配置文件获取到API URL");
                    return false;
                }

                var handler = new HttpClientHandler()
                {
                    AllowAutoRedirect = true
                };
                var httpClient = new HttpClient(handler);

                var testResponse = await _httpClient.GetAsync(_apiUrl);

                return testResponse.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初始化API URL失败: {ex.Message}");
                return false;
            }
        }

        private string? GetApiUrlFromConfig()
        {
            try
            {
                if (!File.Exists("config.xml")) return null;

                var doc = XDocument.Load("config.xml");
                return doc.Descendants("add")
                         .FirstOrDefault(x => x.Attribute("key")?.Value == "ApiUrl")
                         ?.Attribute("value")?.Value;
            }
            catch
            {
                return null;
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