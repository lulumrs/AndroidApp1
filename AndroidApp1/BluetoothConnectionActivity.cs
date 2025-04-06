using System;
using System.Collections.Generic;
using Android.App;
using Android.OS;
using Android.Widget;
using AndroidApp1.Services;
using Plugin.BLE.Abstractions.Contracts;
using Android.Content;
using Android.Runtime;
using Android.Content.PM;
using System.Text;
using Android.Views; // Add this for FocusSearchDirection

namespace AndroidApp1
{
    [Activity(Label = "@string/app_name", MainLauncher = true)]
    public class BluetoothConnectionActivity : Activity
    {
        private BluetoothService _bluetoothService;
        private Button _btnScanDevices;
        private Button _btnConnect;
        private ListView _lvDevices;
        private TextView _tvDebugLogs; // Added for debug logging
        private ScrollView _svDebugLogs; // Scroll view for logs
        private List<string> _deviceNames = new();
        private IDevice? _selectedDevice;
        private StringBuilder _logBuilder = new StringBuilder(); // To store log entries

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_bluetooth_connection);

            // Initialize UI elements
            _btnScanDevices = FindViewById<Button>(Resource.Id.btnScanDevices);
            _btnConnect = FindViewById<Button>(Resource.Id.btnConnect);
            _lvDevices = FindViewById<ListView>(Resource.Id.lvDevices);
            _tvDebugLogs = FindViewById<TextView>(Resource.Id.tvDebugLogs); // Find the debug logs TextView
            _svDebugLogs = FindViewById<ScrollView>(Resource.Id.svDebugLogs); // Find the ScrollView

            // Clear log display
            _logBuilder.Clear();
            UpdateDebugLogs("Debug logging started");

            // Initialize Bluetooth Service
            _bluetoothService = new BluetoothService();
            _bluetoothService.DeviceDiscovered += OnDeviceDiscovered;
            _bluetoothService.StatusChanged += OnStatusChanged;
            _bluetoothService.ErrorOccurred += OnErrorOccurred;

            // Set up UI event handlers
            _btnScanDevices.Click += async (s, e) =>
            {
                _deviceNames.Clear();
                UpdateDeviceList();
                _btnConnect.Enabled = false;
                await _bluetoothService.StartScanningForDevicesAsync();
                UpdateDebugLogs("Started scanning for devices");
            };

            _btnConnect.Click += async (s, e) =>
            {
                if (_selectedDevice != null)
                {
                    await _bluetoothService.ConnectToDeviceAsync(_selectedDevice);
                    
                    // Set the singleton instance that RobotControlActivity will use
                    RobotControlActivity.BluetoothSingleton.Instance = _bluetoothService;
                    
                    // Start RobotControlActivity and pass the device name
                    var intent = new Intent(this, typeof(RobotControlActivity));
                    StartActivity(intent);
                    UpdateDebugLogs($"Connected to device: {_selectedDevice.Name}");
                }
            };

            _lvDevices.ItemClick += (s, e) =>
            {
                _selectedDevice = _bluetoothService.DeviceList[e.Position];
                _btnConnect.Enabled = true;
                Toast.MakeText(this, $"Selected: {_selectedDevice.Name}", ToastLength.Short)?.Show();
                UpdateDebugLogs($"Selected device: {_selectedDevice.Name}");
            };

            // Request permissions if needed (for Android 6.0 and above)
            RequestPermissions();
        }

        private void OnDeviceDiscovered(object? sender, IDevice device)
        {
            RunOnUiThread(() =>
            {
                if (!string.IsNullOrEmpty(device.Name))
                {
                    _deviceNames.Add($"{device.Name} - {device.Id}");
                    UpdateDeviceList();
                    UpdateDebugLogs($"Discovered device: {device.Name} - {device.Id}");
                }
            });
        }

        private void OnStatusChanged(object? sender, string status)
        {
            RunOnUiThread(() => 
            {
                Toast.MakeText(this, status, ToastLength.Short)?.Show();
                UpdateDebugLogs($"Status changed: {status}");
            });
        }

        private void OnErrorOccurred(object? sender, string error)
        {
            RunOnUiThread(() => 
            {
                Toast.MakeText(this, error, ToastLength.Long)?.Show();
                UpdateDebugLogs($"Error occurred: {error}");
            });
        }

        private void UpdateDeviceList()
        {
            var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleListItem1, _deviceNames);
            _lvDevices.Adapter = adapter;
        }

        private void RequestPermissions()
        {
            const int locationPermissionRequestCode = 1000;
            var requiredPermissions = new[]
            {
                Android.Manifest.Permission.AccessFineLocation,
                Android.Manifest.Permission.AccessCoarseLocation,
                Android.Manifest.Permission.Bluetooth,
                Android.Manifest.Permission.BluetoothAdmin
            };

            // For Android 12+ we need these additional permissions
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            {
                requiredPermissions = new[]
                {
                    Android.Manifest.Permission.AccessFineLocation,
                    Android.Manifest.Permission.AccessCoarseLocation,
                    Android.Manifest.Permission.Bluetooth,
                    Android.Manifest.Permission.BluetoothAdmin,
                    Android.Manifest.Permission.BluetoothScan,
                    Android.Manifest.Permission.BluetoothConnect
                };
            }

            RequestPermissions(requiredPermissions, locationPermissionRequestCode);
        }

        private void UpdateDebugLogs(string message)
        {
            _logBuilder.AppendLine($"{DateTime.Now}: {message}");
            _tvDebugLogs.Text = _logBuilder.ToString();
            _svDebugLogs.Post(() => _svDebugLogs.FullScroll(FocusSearchDirection.Down));
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _bluetoothService.StopScanning();
            _bluetoothService.DeviceDiscovered -= OnDeviceDiscovered;
            _bluetoothService.StatusChanged -= OnStatusChanged;
            _bluetoothService.ErrorOccurred -= OnErrorOccurred;
        }
    }
}