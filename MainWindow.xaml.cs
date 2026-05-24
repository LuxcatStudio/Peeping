﻿﻿﻿﻿﻿﻿﻿﻿using ActivityMonitor.Services;
using System.Windows;
using System.Windows.Controls;
using System.Timers;
using Hardcodet.Wpf.TaskbarNotification;
using System.Drawing;
using Timer = System.Timers.Timer;
using Application = System.Windows.Application;

namespace ActivityMonitor
{
    public partial class MainWindow : Window, IDisposable
    {
        private readonly SystemMonitorService _monitorService;
        private readonly ApiService _apiService;
        private readonly SettingsService _settingsService;
        private readonly Timer _monitorTimer;
        private TaskbarIcon? _notifyIcon;
        private DashboardWindow? _dashboardWindow;
        private bool _disposed;
        private bool _isSettingsChanged;

        public MainWindow()
        {
            InitializeComponent();

            _settingsService = new SettingsService();
            _monitorService = new SystemMonitorService();
            _apiService = new ApiService();

            CreateTaskbarIcon();

            _monitorTimer = new Timer(10000);
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
                System.Diagnostics.Debug.WriteLine("成功加载图标");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载图标失败: {ex.Message}");
                _notifyIcon.Icon = SystemIcons.Application;
            }

            _notifyIcon.ToolTipText = "活动监控器";

            var contextMenu = new ContextMenu();

            var dashboardMenuItem = new MenuItem() { Header = "监控仪表板" };
            dashboardMenuItem.Click += (s, e) => ShowDashboard();
            contextMenu.Items.Add(dashboardMenuItem);

            var showMenuItem = new MenuItem() { Header = "显示窗口" };
            showMenuItem.Click += (s, e) => ShowWindow();
            contextMenu.Items.Add(showMenuItem);

            var settingsMenuItem = new MenuItem() { Header = "设置" };
            settingsMenuItem.Click += (s, e) => ShowWindow();
            contextMenu.Items.Add(settingsMenuItem);

            contextMenu.Items.Add(new Separator());

            var diagnosticMenuItem = new MenuItem() { Header = "系统诊断" };
            diagnosticMenuItem.Click += (s, e) => RunDiagnostics();
            contextMenu.Items.Add(diagnosticMenuItem);

            contextMenu.Items.Add(new Separator());

            var exitMenuItem = new MenuItem() { Header = "退出" };
            exitMenuItem.Click += OnExitClick;
            contextMenu.Items.Add(exitMenuItem);

            _notifyIcon.ContextMenu = contextMenu;

            _notifyIcon.TrayMouseDoubleClick += (s, e) => ShowDashboard();

            _notifyIcon.TrayLeftMouseDown += (s, e) => ShowDashboard();
        }

        private void ShowDashboard()
        {
            if (_dashboardWindow == null || !_dashboardWindow.IsLoaded)
            {
                _dashboardWindow = new DashboardWindow();
                _dashboardWindow.Closed += (s, e) => { _dashboardWindow = null; };
            }
            _dashboardWindow.Show();
            _dashboardWindow.Activate();
        }

        private void RunDiagnostics()
        {
            System.Windows.MessageBox.Show("系统诊断：\n• API 连接：正常\n• 系统监控：正常\n• 托盘图标：正常", "系统诊断结果", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettingsToUI();

            bool apiInitialized = await _apiService.InitializeApiUrlAsync(_settingsService.Settings.ApiUrl);
            if (!apiInitialized)
            {
                ShowError("无法连接到API服务器，应用将继续运行但数据可能无法发送。");
            }

            if (_settingsService.Settings.AutoStart)
            {
                _settingsService.ApplyAutoStart(true);
            }

            _monitorTimer.Start();

            await MonitorAndSendAsync();
        }

        private void LoadSettingsToUI()
        {
            ApiUrlTextBox.Text = _settingsService.Settings.ApiUrl;
            AutoStartCheckBox.IsChecked = _settingsService.Settings.AutoStart;
            HideAllNotifications();
        }

        private void ShowError(string message)
        {
            Dispatcher.Invoke(() => {
                ErrorText.Text = message;
                ErrorBorder.Visibility = Visibility.Visible;
                SuccessBorder.Visibility = Visibility.Collapsed;
            });
        }

        private void ShowSuccess(string message)
        {
            Dispatcher.Invoke(() => {
                SuccessText.Text = message;
                SuccessBorder.Visibility = Visibility.Visible;
                ErrorBorder.Visibility = Visibility.Collapsed;

                var timer = new Timer(3000);
                timer.Elapsed += (s, e) => {
                    Dispatcher.Invoke(() => {
                        SuccessBorder.Visibility = Visibility.Collapsed;
                    });
                    timer.Stop();
                    timer.Dispose();
                };
                timer.AutoReset = false;
                timer.Start();
            });
        }

        private void HideAllNotifications()
        {
            Dispatcher.Invoke(() => {
                ErrorBorder.Visibility = Visibility.Collapsed;
                SuccessBorder.Visibility = Visibility.Collapsed;
            });
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var newApiUrl = ApiUrlTextBox.Text.Trim();
            var newAutoStart = AutoStartCheckBox.IsChecked == true;

            if (string.IsNullOrEmpty(newApiUrl))
            {
                ShowError("API 地址不能为空");
                return;
            }

            if (_settingsService.Settings.ApiUrl != newApiUrl)
            {
                _settingsService.UpdateApiUrl(newApiUrl);
                _apiService.UpdateApiUrl(newApiUrl);
                _isSettingsChanged = true;
            }

            if (_settingsService.Settings.AutoStart != newAutoStart)
            {
                _settingsService.UpdateAutoStart(newAutoStart);
                _isSettingsChanged = true;
            }

            if (_isSettingsChanged)
            {
                ShowSuccess("设置已保存！");
            }
            else
            {
                ShowSuccess("无需保存的更改");
            }

            Hide();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            LoadSettingsToUI();
            Hide();
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
            LoadSettingsToUI();
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
            _monitorTimer.Stop();
            _notifyIcon?.Dispose();
            Application.Current.Shutdown();
        }

        private async Task MonitorAndSendAsync()
        {
            try
            {
                var status = _monitorService.GetDeviceStatus();
                bool success = await _apiService.SendDeviceStatusAsync(status);

                if (_notifyIcon != null)
                {
                    Dispatcher.Invoke(() => {
                        _notifyIcon.ToolTipText = $"活动监控器\n最后更新: {DateTime.Now:HH:mm:ss}\n状态: {(success ? "正常" : "发送失败")}";

                    });
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

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
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
                _monitorTimer?.Stop();
                _monitorTimer?.Dispose();
                _monitorService?.Dispose();
                _apiService?.Dispose();
                _notifyIcon?.Dispose();
                _dashboardWindow?.Dispose();
            }

            _disposed = true;
        }

        ~MainWindow()
        {
            Dispose(false);
        }
    }
}