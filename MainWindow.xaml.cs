using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PCToMobile.Models;
using PCToMobile.Services;

namespace PCToMobile;

public partial class MainWindow : Window
{
    private static readonly Brush MutedStatusBrush = new SolidColorBrush(Color.FromRgb(104, 116, 125));
    private static readonly Brush SuccessStatusBrush = new SolidColorBrush(Color.FromRgb(36, 122, 82));
    private static readonly Brush WarningStatusBrush = new SolidColorBrush(Color.FromRgb(183, 101, 18));
    private static readonly Brush DangerStatusBrush = new SolidColorBrush(Color.FromRgb(179, 58, 58));

    private readonly ToolLocator _tools = new();
    private readonly AdbService _adb;
    private readonly ScrcpyService _scrcpy;
    private readonly QrPairingService _qrPairing;
    private readonly DispatcherTimer _deviceMonitorTimer;
    private CancellationTokenSource? _qrPairingCancellation;
    private bool _busy;
    private bool _checkingDeviceConnections;

    public ObservableCollection<AndroidDevice> Devices { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _adb = new AdbService(_tools);
        _scrcpy = new ScrcpyService(_tools);
        _qrPairing = new QrPairingService(_adb);
        _scrcpy.SessionEnded += Scrcpy_SessionEnded;
        _deviceMonitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _deviceMonitorTimer.Tick += DeviceMonitorTimer_Tick;

        Loaded += async (_, _) =>
        {
            CheckTools();
            await RefreshDevicesAsync();
            _deviceMonitorTimer.Start();
        };
    }

    private AndroidDevice? SelectedDevice => DeviceList.SelectedItem as AndroidDevice;

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        CheckTools();
        await RefreshDevicesAsync();
    }

    private async void DeviceMonitorTimer_Tick(object? sender, EventArgs e)
    {
        await MonitorDeviceConnectionsAsync();
    }

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedDevice();
    }

    private void MirrorButton_Click(object sender, RoutedEventArgs e)
    {
        var device = SelectedDevice;
        if (device is null)
        {
            SetStatus("Hãy chọn một thiết bị.", StatusKind.Warning);
            return;
        }

        try
        {
            var options = new MirrorOptions
            {
                MaxSize = GetComboValue(MaxSizeCombo, 1920),
                MaxFps = GetComboValue(FpsCombo, 60),
                BitRateMbps = (int)BitRateSlider.Value,
                ForwardAudio = AudioCheck.IsChecked == true,
                StayAwake = StayAwakeCheck.IsChecked == true,
                TurnScreenOff = ScreenOffCheck.IsChecked == true,
                RecordPath = RecordCheck.IsChecked == true ? CreateRecordingPath() : null
            };

            _scrcpy.Start(device, options);
            MirrorButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            Log($"Đã mở phiên điều khiển cho {device.Serial}.");
            SetStatus($"Đang điều khiển {device.DisplayName}.", StatusKind.Success);
        }
        catch (Exception ex)
        {
            LogError(ex);
            SetStatus(ex.Message, StatusKind.Error);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        var device = SelectedDevice;
        if (device is null)
        {
            return;
        }

        _scrcpy.Stop(device.Serial);
        UpdateSelectedDevice();
        Log($"Đã dừng phiên {device.Serial}.");
        SetStatus("Đã dừng phiên điều khiển.", StatusKind.Neutral);
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAdbActionAsync(
            "Đang kết nối qua Wi-Fi...",
            () => _adb.ConnectAsync(ConnectEndpointText.Text));
    }

    private async void PairButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAdbActionAsync(
            "Đang ghép đôi...",
            () => _adb.PairAsync(PairEndpointText.Text, PairCodeText.Text));
    }

    private async void StartQrPairingButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_tools.HasAdb)
        {
            SetStatus("Thiếu adb.exe trong thư mục tools.", StatusKind.Warning);
            return;
        }

        CancelQrPairing();
        var session = _qrPairing.CreateSession();
        _qrPairingCancellation = new CancellationTokenSource();
        QrPairingImage.Source = CreateBitmapImage(session.QrPng);
        QrPlaceholder.Visibility = Visibility.Collapsed;
        StartQrPairingButton.IsEnabled = false;
        CancelQrPairingButton.IsEnabled = true;
        QrPairingStatusText.Text = "Đang chờ điện thoại quét...";
        QrPairingDetailText.Text = session.ServiceName;
        SetStatus("Đang chờ quét mã QR.", StatusKind.Neutral);
        Log($"Đã tạo phiên QR {session.ServiceName}.");

        var progress = new Progress<string>(message =>
        {
            QrPairingStatusText.Text = message;
            SetStatus(message, StatusKind.Neutral);
        });

        try
        {
            var result = await _qrPairing.PairAsync(
                session,
                progress,
                _qrPairingCancellation.Token);

            Log(result.Message);
            QrPairingStatusText.Text = result.Message;

            if (result.Success)
            {
                QrPairingDetailText.Text =
                    result.ConnectEndpoint ?? "Thiết bị đã được ghép đôi";
                if (!string.IsNullOrWhiteSpace(result.ConnectEndpoint))
                {
                    ConnectEndpointText.Text = result.ConnectEndpoint;
                }

                SetStatus(result.Message, StatusKind.Success);
                await RefreshDevicesAsync();
            }
            else
            {
                QrPairingDetailText.Text = "Tạo mã mới để thử lại";
                SetStatus(result.Message, StatusKind.Error);
            }
        }
        catch (OperationCanceledException)
        {
            QrPairingStatusText.Text = "Đã hủy phiên ghép đôi";
            QrPairingDetailText.Text = "ADB Wireless Debugging";
            SetStatus("Đã hủy chờ quét QR.", StatusKind.Neutral);
            Log("Đã hủy phiên QR.");
        }
        catch (Exception ex)
        {
            QrPairingStatusText.Text = "Ghép đôi QR không thành công";
            QrPairingDetailText.Text = ex.Message;
            LogError(ex);
            SetStatus(ex.Message, StatusKind.Error);
        }
        finally
        {
            _qrPairingCancellation?.Dispose();
            _qrPairingCancellation = null;
            StartQrPairingButton.IsEnabled = true;
            CancelQrPairingButton.IsEnabled = false;
        }
    }

    private void CancelQrPairingButton_Click(object sender, RoutedEventArgs e)
    {
        CancelQrPairing();
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        var device = SelectedDevice;
        if (device is null || !device.IsWireless)
        {
            SetStatus("Hãy chọn một thiết bị kết nối qua Wi-Fi.", StatusKind.Warning);
            return;
        }

        await RunAdbActionAsync(
            "Đang ngắt kết nối...",
            () => _adb.DisconnectAsync(device.Serial));
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        LogText.Clear();
    }

    private void BitRateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BitRateText is not null)
        {
            BitRateText.Text = $"{(int)e.NewValue}M";
        }
    }

    private async Task RefreshDevicesAsync()
    {
        if (_busy || _checkingDeviceConnections)
        {
            return;
        }

        if (!_tools.HasAdb)
        {
            _scrcpy.StopAll();
            Devices.Clear();
            UpdateDeviceListState();
            SetStatus("Thiếu adb.exe trong thư mục tools.", StatusKind.Warning);
            return;
        }

        var previousSerial = SelectedDevice?.Serial;
        SetBusy(true);
        SetStatus("Đang tìm thiết bị...", StatusKind.Neutral);

        try
        {
            var devices = await _adb.GetDevicesAsync();
            _scrcpy.StopUnavailableSessions(devices);
            Devices.Clear();
            foreach (var device in devices)
            {
                Devices.Add(device);
            }

            DeviceList.SelectedItem =
                Devices.FirstOrDefault(device => device.Serial == previousSerial) ??
                Devices.FirstOrDefault();

            UpdateDeviceListState();
            Log($"Tìm thấy {Devices.Count} thiết bị.");

            if (Devices.Count == 0)
            {
                SetStatus("Chưa phát hiện thiết bị Android.", StatusKind.Neutral);
            }
            else if (Devices.Any(device => !device.IsReady))
            {
                SetStatus("Có thiết bị đang chờ cấp quyền hoặc offline.", StatusKind.Warning);
            }
            else
            {
                SetStatus($"Đã kết nối {Devices.Count} thiết bị.", StatusKind.Success);
            }
        }
        catch (Exception ex)
        {
            _scrcpy.StopAll();
            Devices.Clear();
            UpdateDeviceListState();
            LogError(ex);
            SetStatus(ex.Message, StatusKind.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateSelectedDevice();
        }
    }

    private async Task MonitorDeviceConnectionsAsync()
    {
        if (_busy || _checkingDeviceConnections || !_tools.HasAdb)
        {
            return;
        }

        _checkingDeviceConnections = true;
        var previousSerial = SelectedDevice?.Serial;
        var previousDevices = Devices.ToArray();

        try
        {
            var detectedDevices = await _adb.GetDevicesAsync();
            _scrcpy.StopUnavailableSessions(detectedDevices);
            var connectedDevices = detectedDevices
                .Where(device =>
                    !device.State.Equals("offline", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var disconnectedDevices = previousDevices
                .Where(previous => connectedDevices.All(current =>
                    !current.Serial.Equals(previous.Serial, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            var unavailableDevices = previousDevices
                .Where(previous => previous.IsReady)
                .Where(previous => connectedDevices.Any(current =>
                    current.Serial.Equals(previous.Serial, StringComparison.OrdinalIgnoreCase) &&
                    !current.IsReady))
                .ToArray();

            foreach (var device in disconnectedDevices.Concat(unavailableDevices))
            {
                _scrcpy.Stop(device.Serial);
            }

            if (!DeviceListsMatch(previousDevices, connectedDevices))
            {
                Devices.Clear();
                foreach (var device in connectedDevices)
                {
                    Devices.Add(device);
                }

                DeviceList.SelectedItem =
                    Devices.FirstOrDefault(device =>
                        device.Serial.Equals(previousSerial, StringComparison.OrdinalIgnoreCase)) ??
                    Devices.FirstOrDefault();
                UpdateDeviceListState();
            }

            if (disconnectedDevices.Length > 0)
            {
                var serials = string.Join(", ", disconnectedDevices.Select(device => device.Serial));
                Log($"Thiết bị đã ngắt kết nối: {serials}.");
                SetStatus(
                    "Thiết bị đã ngắt kết nối và phiên điều khiển đã được đóng.",
                    StatusKind.Warning);
            }
        }
        catch (Exception ex)
        {
            _scrcpy.StopAll();
            if (Devices.Count > 0)
            {
                foreach (var device in Devices.ToArray())
                {
                    _scrcpy.Stop(device.Serial);
                }

                Devices.Clear();
                DeviceList.SelectedItem = null;
                UpdateDeviceListState();
                LogError(ex);
                SetStatus(
                    "Không còn xác nhận được kết nối thiết bị. Phiên điều khiển đã được đóng.",
                    StatusKind.Error);
            }
        }
        finally
        {
            _checkingDeviceConnections = false;
            UpdateSelectedDevice();
        }
    }

    private static bool DeviceListsMatch(
        IReadOnlyList<AndroidDevice> first,
        IReadOnlyList<AndroidDevice> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        return first.Zip(second).All(pair =>
            pair.First.Serial.Equals(pair.Second.Serial, StringComparison.OrdinalIgnoreCase) &&
            pair.First.State.Equals(pair.Second.State, StringComparison.OrdinalIgnoreCase) &&
            pair.First.Model.Equals(pair.Second.Model, StringComparison.Ordinal));
    }

    private async Task RunAdbActionAsync(
        string pendingMessage,
        Func<Task<CommandResult>> action)
    {
        if (_busy || _checkingDeviceConnections)
        {
            return;
        }

        SetBusy(true);
        SetStatus(pendingMessage, StatusKind.Neutral);
        try
        {
            var result = await action();
            var message = string.IsNullOrWhiteSpace(result.BestMessage)
                ? (result.Success ? "Hoàn tất." : "Thao tác không thành công.")
                : result.BestMessage;

            Log(message);
            SetStatus(message, result.Success ? StatusKind.Success : StatusKind.Error);

            if (result.Success)
            {
                await Task.Delay(350);
            }
        }
        catch (Exception ex)
        {
            LogError(ex);
            SetStatus(ex.Message, StatusKind.Error);
        }
        finally
        {
            SetBusy(false);
        }

        await RefreshDevicesAsync();
    }

    private void CheckTools()
    {
        _tools.Refresh();

        if (_tools.IsReady)
        {
            ToolStatusDot.Fill = SuccessStatusBrush;
            ToolStatusText.Text = "ADB + scrcpy sẵn sàng";
            Log("Đã tìm thấy ADB và scrcpy.");
        }
        else
        {
            ToolStatusDot.Fill = WarningStatusBrush;
            ToolStatusText.Text =
                !_tools.HasAdb && !_tools.HasScrcpy ? "Thiếu ADB và scrcpy" :
                !_tools.HasAdb ? "Thiếu ADB" :
                "Thiếu scrcpy";
            Log($"Công cụ: ADB={_tools.HasAdb}, scrcpy={_tools.HasScrcpy}.");
        }
    }

    private void UpdateSelectedDevice()
    {
        var device = SelectedDevice;
        if (device is null)
        {
            SelectedDeviceName.Text = "Chọn một thiết bị";
            SelectedDeviceDetail.Text = "USB hoặc Wi-Fi";
            MirrorButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            DisconnectButton.IsEnabled = false;
            return;
        }

        SelectedDeviceName.Text = device.DisplayName;
        SelectedDeviceDetail.Text =
            $"{device.ConnectionLabel}  ·  {device.Serial}  ·  {GetStateLabel(device.State)}";

        var running = _scrcpy.IsRunning(device.Serial);
        MirrorButton.IsEnabled = device.IsReady && _tools.HasScrcpy && !running && !_busy;
        StopButton.IsEnabled = running;
        DisconnectButton.IsEnabled =
            device.IsWireless && !_busy;
    }

    private void UpdateDeviceListState()
    {
        DeviceCountText.Text = Devices.Count.ToString();
        EmptyDeviceState.Visibility =
            Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        DeviceList.IsEnabled = !busy;
        UpdateSelectedDevice();
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusText.Text = message.ReplaceLineEndings(" ");
        StatusDot.Fill = kind switch
        {
            StatusKind.Success => SuccessStatusBrush,
            StatusKind.Warning => WarningStatusBrush,
            StatusKind.Error => DangerStatusBrush,
            _ => MutedStatusBrush
        };
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}{Environment.NewLine}";
        LogText.AppendText(line);
        LogText.ScrollToEnd();
    }

    private void LogError(Exception exception)
    {
        Log($"LỖI: {exception.Message}");
    }

    private void Scrcpy_SessionEnded(object? sender, string serial)
    {
        Dispatcher.Invoke(() =>
        {
            Log($"Phiên {serial} đã kết thúc.");
            UpdateSelectedDevice();
            SetStatus("Phiên điều khiển đã kết thúc.", StatusKind.Neutral);
        });
    }

    private static int GetComboValue(ComboBox comboBox, int fallback)
    {
        if (comboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var value))
        {
            return value;
        }

        return fallback;
    }

    private static string GetStateLabel(string state) =>
        state.ToLowerInvariant() switch
        {
            "device" => "Sẵn sàng",
            "unauthorized" => "Chờ cấp quyền",
            "offline" => "Offline",
            _ => state
        };

    private static string CreateRecordingPath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "PCToMobile");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"phone-{DateTime.Now:yyyyMMdd-HHmmss}.mp4");
    }

    private static BitmapImage CreateBitmapImage(byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void CancelQrPairing()
    {
        _qrPairingCancellation?.Cancel();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _deviceMonitorTimer.Stop();
        _deviceMonitorTimer.Tick -= DeviceMonitorTimer_Tick;
        CancelQrPairing();
        _scrcpy.SessionEnded -= Scrcpy_SessionEnded;
        _scrcpy.Dispose();
    }

    private enum StatusKind
    {
        Neutral,
        Success,
        Warning,
        Error
    }
}
