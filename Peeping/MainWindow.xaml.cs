using ActivityMonitor.Services;
using System.Windows;
using System.Windows.Controls;
using System.Timers;
using Hardcodet.Wpf.TaskbarNotification;
using System.Drawing;
using Timer = System.Timers.Timer;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace ActivityMonitor
{
    public partial class MainWindow : Window
    {
        private readonly SystemMonitorService _monitorService;
        private readonly ApiService _apiService;
        private readonly Timer _monitorTimer;
        private TaskbarIcon? _notifyIcon;

        public MainWindow()
        {
            InitializeComponent();

            _monitorService = new SystemMonitorService();
            _apiService = new ApiService();

            CreateTaskbarIcon();

            _monitorTimer = new Timer(30000);
            _monitorTimer.Elapsed += async (s, e) => await MonitorAndSendAsync();
            _monitorTimer.AutoReset = true;
        }

        private void CreateTaskbarIcon()
        {
            _notifyIcon = new TaskbarIcon();

            try
            {
                using var icon = IconService.GetApplicationIcon();
                _notifyIcon.Icon = icon;
                System.Diagnostics.Debug.WriteLine("成功从Base64加载图标");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载图标失败: {ex.Message}");
                _notifyIcon.Icon = SystemIcons.Application;
            }

            _notifyIcon.ToolTipText = "活动监控器";

            var contextMenu = new ContextMenu();

            var showMenuItem = new MenuItem() { Header = "显示窗口" };
            showMenuItem.Click += (s, e) => ShowWindow();
            contextMenu.Items.Add(showMenuItem);

            var hideMenuItem = new MenuItem() { Header = "隐藏窗口" };
            hideMenuItem.Click += (s, e) => HideWindow();
            contextMenu.Items.Add(hideMenuItem);

            contextMenu.Items.Add(new Separator());

            var exitMenuItem = new MenuItem() { Header = "退出" };
            exitMenuItem.Click += OnExitClick;
            contextMenu.Items.Add(exitMenuItem);

            _notifyIcon.ContextMenu = contextMenu;

            _notifyIcon.TrayMouseDoubleClick += (s, e) => ToggleWindowVisibility();

            _notifyIcon.TrayLeftMouseDown += (s, e) => ToggleWindowVisibility();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {

            Hide();

            bool apiInitialized = await _apiService.InitializeApiUrlAsync();
            if (!apiInitialized)
            {
                ShowNotification("警告", "无法连接到API服务器，应用将继续运行但数据可能无法发送。");
            }

            SetStartup();

            _monitorTimer.Start();

            await MonitorAndSendAsync();

            ShowNotification("活动监控器", "应用已启动并在后台运行");
        }

        private void ToggleWindowVisibility()
        {
            if (IsVisible)
            {
                HideWindow();
            }
            else
            {
                ShowWindow();
            }
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            BringIntoView();
        }

        private void HideWindow()
        {
            Hide();
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确定要退出活动监控器吗？", "确认退出",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _monitorTimer.Stop();
                _notifyIcon?.Dispose();
                Application.Current.Shutdown();
            }
        }

        private async Task MonitorAndSendAsync()
        {
            try
            {
                var status = _monitorService.GetDeviceStatus();
                bool success = await _apiService.SendDeviceStatusAsync(status);

                if (_notifyIcon != null)
                {
                    _notifyIcon.ToolTipText = $"活动监控器\n最后更新: {DateTime.Now:HH:mm:ss}\n状态: {(success ? "正常" : "发送失败")}";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"监控发送错误: {ex.Message}");

                if (_notifyIcon != null)
                {
                    _notifyIcon.ToolTipText = $"活动监控器\n最后更新: {DateTime.Now:HH:mm:ss}\n状态: 错误 - {ex.Message}";
                }
            }
        }

        private void SetStartup()
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
                        key.SetValue("ActivityMonitor", $"\"{appPath}\"");
                        System.Diagnostics.Debug.WriteLine("开机自启动已设置");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置开机自启动失败: {ex.Message}");
            }
        }

        private void ShowNotification(string title, string message)
        {
            _notifyIcon?.ShowBalloonTip(title, message, BalloonIcon.Info);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _monitorTimer?.Stop();
            _monitorTimer?.Dispose();
            _monitorService?.Dispose();
            _notifyIcon?.Dispose();

            base.OnClosing(e);
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        }
    }
}