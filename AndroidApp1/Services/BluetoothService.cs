using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Util; // Add Android logging
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Exceptions;

namespace AndroidApp1.Services
{
    public class BluetoothService
    {
        private const string TAG = "BluetoothService"; // Tag for logging
        // ESP32 BLE UUIDs - must match the ones defined in the ESP32 code
        private static readonly Guid ESP32_SERVICE_UUID = Guid.Parse("4FAFC201-1FB5-459E-8FCC-C5C9C331914B");
        private static readonly Guid ESP32_CHARACTERISTIC_UUID = Guid.Parse("BEB5483E-36E1-4688-B7F5-EA07361B26A8");

        private readonly IBluetoothLE _bluetoothLE;
        private readonly Plugin.BLE.Abstractions.Contracts.IAdapter _adapter;
        private IDevice? _connectedDevice;
        private IService? _gattService;
        private ICharacteristic? _writeCharacteristic;
        private List<IDevice> _deviceList;
        private CancellationTokenSource? _cancellationTokenSource;
        private string _lastCommand = "";

        // Events
        public event EventHandler<IDevice> DeviceDiscovered;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<string> ErrorOccurred;
        public event EventHandler<bool> ConnectionStatusChanged;

        public List<IDevice> DeviceList => _deviceList;
        public bool IsConnected => _connectedDevice != null && _adapter.ConnectedDevices.Contains(_connectedDevice);

        public BluetoothService()
        {
            _bluetoothLE = CrossBluetoothLE.Current;
            _adapter = CrossBluetoothLE.Current.Adapter;
            _deviceList = new List<IDevice>();

            _adapter.DeviceDiscovered += OnDeviceDiscovered;
            _adapter.DeviceConnected += OnDeviceConnected;
            _adapter.DeviceDisconnected += OnDeviceDisconnected;
            _adapter.DeviceConnectionLost += OnDeviceConnectionLost;
        }

        private void OnDeviceDiscovered(object sender, DeviceEventArgs e)
        {
            var device = e.Device;
            Log.Debug(TAG, $"Device discovered - Name: {device.Name ?? "null"}, ID: {device.Id}");
            Log.Debug(TAG, $"  RSSI: {device.Rssi}, State: {device.State}");

            // Try to get advertised service UUIDs
            if (device.AdvertisementRecords != null)
            {
                Log.Debug(TAG, $"  Advertisement records count: {device.AdvertisementRecords.Count()}");
                foreach (var record in device.AdvertisementRecords)
                {
                    Log.Debug(TAG, $"  Ad Record - Type: {record.Type}, Data: {BitConverter.ToString(record.Data ?? new byte[0])}");
                }
            }
            else
            {
                Log.Debug(TAG, "  No advertisement records");
            }

            if (!_deviceList.Any(d => d.Id == device.Id))
            {
                _deviceList.Add(device);
                Log.Debug(TAG, $"Added device to list. Total count: {_deviceList.Count}");
                DeviceDiscovered?.Invoke(this, device);
            }
            else
            {
                Log.Debug(TAG, "Device already in list, not adding duplicate");
            }
        }

        private void OnDeviceConnected(object sender, DeviceEventArgs e)
        {
            StatusChanged?.Invoke(this, "Connected to " + e.Device.Name);
            ConnectionStatusChanged?.Invoke(this, true);
        }

        private void OnDeviceDisconnected(object sender, DeviceEventArgs e)
        {
            StatusChanged?.Invoke(this, "Disconnected from " + e.Device.Name);
            ConnectionStatusChanged?.Invoke(this, false);
            _connectedDevice = null;
            _gattService = null;
            _writeCharacteristic = null;
        }

        private void OnDeviceConnectionLost(object sender, DeviceErrorEventArgs e)
        {
            ErrorOccurred?.Invoke(this, "Connection lost: " + e.ErrorMessage);
            ConnectionStatusChanged?.Invoke(this, false);
            _connectedDevice = null;
            _gattService = null;
            _writeCharacteristic = null;
        }

        public async Task StartScanningForDevicesAsync()
        {
            if (!_bluetoothLE.IsAvailable)
            {
                Log.Error(TAG, "Bluetooth is not available on this device");
                ErrorOccurred?.Invoke(this, "Bluetooth is not available");
                return;
            }

            if (_bluetoothLE.State != BluetoothState.On)
            {
                Log.Error(TAG, $"Bluetooth is not turned on. Current state: {_bluetoothLE.State}");
                ErrorOccurred?.Invoke(this, "Bluetooth is not turned on");
                return;
            }

            try
            {
                // Log adapter state before scanning
                Log.Debug(TAG, $"Adapter state before scan - IsScanning: {_adapter.IsScanning}");
                
                // Check if adapter is already scanning
                if (_adapter.IsScanning)
                {
                    Log.Debug(TAG, "Stopping previous scan before starting new one");
                    await _adapter.StopScanningForDevicesAsync();
                }
                
                _deviceList.Clear();
                StatusChanged?.Invoke(this, "Scanning for devices...");
                Log.Debug(TAG, "Scanning for devices started");
                
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();

                // Log already connected devices
                var connectedDevices = _adapter.ConnectedDevices;
                Log.Debug(TAG, $"Currently connected devices count: {connectedDevices.Count}");
                foreach (var device in connectedDevices)
                {
                    Log.Debug(TAG, $"Already connected device: {device.Name} (ID: {device.Id})");
                }
                
                // CHANGE: First try scanning WITHOUT service UUID filter
                Log.Debug(TAG, "Starting scan without service UUID filter first");
                StatusChanged?.Invoke(this, "Scanning for all BLE devices...");
                
                // Fix ambiguous call by explicitly using a null Guid array for the first parameter
                await _adapter.StartScanningForDevicesAsync(
                    new Guid[] { }, // Empty array instead of null to avoid ambiguity
                    null, // Allow duplicate readings
                    false, // Don't allow scanning when screen is off
                    _cancellationTokenSource.Token);
                
                Log.Debug(TAG, $"Generic scan started. Adapter.IsScanning: {_adapter.IsScanning}");
                
                // After 10 seconds, check if we've found devices
                await Task.Delay(10000, _cancellationTokenSource.Token);
                
                if (!_cancellationTokenSource.IsCancellationRequested)
                {
                    Log.Debug(TAG, $"First scan completed. Devices found: {_deviceList.Count}");

                    // Report on devices that match our ESP32 name pattern
                    var matchingDevices = _deviceList.Where(d => d.Name != null && d.Name.Contains("ESP32")).ToList();
                    if (matchingDevices.Any())
                    {
                        Log.Debug(TAG, $"Found {matchingDevices.Count} devices with 'ESP32' in name");
                        foreach (var device in matchingDevices)
                        {
                            Log.Debug(TAG, $"ESP32 device: {device.Name} (ID: {device.Id})");
                        }
                    }
                    else
                    {
                        Log.Debug(TAG, "No devices with 'ESP32' in name found");
                    }

                    // Try scanning with service UUID filter as a second attempt
                    if (_deviceList.Count == 0 || !matchingDevices.Any())
                    {
                        Log.Debug(TAG, "Now trying scan with service UUID filter");
                        StatusChanged?.Invoke(this, "Scanning for ESP32-specific devices...");
                        
                        await _adapter.StopScanningForDevicesAsync();
                        await _adapter.StartScanningForDevicesAsync(
                            new[] { ESP32_SERVICE_UUID }, 
                            null, 
                            false, 
                            cancellationToken: _cancellationTokenSource.Token);
                        
                        Log.Debug(TAG, "Scanning with service UUID filter started");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error scanning for devices: {ex.Message}");
                Log.Error(TAG, $"Stack trace: {ex.StackTrace}");
                ErrorOccurred?.Invoke(this, "Error scanning for devices: " + ex.Message);
            }
        }

        public void StopScanning()
        {
            _cancellationTokenSource?.Cancel();
            _adapter.StopScanningForDevicesAsync();
            StatusChanged?.Invoke(this, "Scan stopped");
            Log.Debug(TAG, "Scan stopped"); // Add logging
        }

        public async Task ConnectToDeviceAsync(IDevice device)
        {
            if (device == null)
                return;
                
            StatusChanged?.Invoke(this, "Connecting to " + device.Name + "...");

            try
            {
                await _adapter.ConnectToDeviceAsync(device);
                _connectedDevice = device;
                
                // Get services
                var services = await _connectedDevice.GetServicesAsync();
                StatusChanged?.Invoke(this, $"Found {services.Count} services");
                
                // Find the ESP32 service using its specific UUID
                _gattService = services.FirstOrDefault(s => s.Id == ESP32_SERVICE_UUID);
                
                if (_gattService != null)
                {
                    StatusChanged?.Invoke(this, "ESP32 service found");
                    var characteristics = await _gattService.GetCharacteristicsAsync();
                    StatusChanged?.Invoke(this, $"Found {characteristics.Count} characteristics");
                    
                    // Find the characteristic using its specific UUID
                    _writeCharacteristic = characteristics.FirstOrDefault(c => c.Id == ESP32_CHARACTERISTIC_UUID);
                    
                    if (_writeCharacteristic != null)
                    {
                        StatusChanged?.Invoke(this, "ESP32 characteristic found");
                        if (_writeCharacteristic.CanWrite)
                        {
                            StatusChanged?.Invoke(this, "Ready to send commands");
                        }
                        else
                        {
                            ErrorOccurred?.Invoke(this, "Characteristic doesn't support writing");
                        }
                    }
                    else
                    {
                        // Fallback to any writable characteristic if the specific one isn't found
                        _writeCharacteristic = characteristics.FirstOrDefault(c => c.CanWrite);
                        
                        if (_writeCharacteristic == null)
                        {
                            ErrorOccurred?.Invoke(this, "Could not find writable characteristic");
                        }
                        else
                        {
                            StatusChanged?.Invoke(this, "Using fallback characteristic");
                        }
                    }
                }
                else
                {
                    // Fallback to first service if the specific one isn't found
                    _gattService = services.FirstOrDefault();
                    
                    if (_gattService != null)
                    {
                        StatusChanged?.Invoke(this, "Using fallback service");
                        var characteristics = await _gattService.GetCharacteristicsAsync();
                        
                        _writeCharacteristic = characteristics.FirstOrDefault(c => c.CanWrite);
                        
                        if (_writeCharacteristic == null)
                        {
                            ErrorOccurred?.Invoke(this, "Could not find writable characteristic");
                        }
                    }
                    else
                    {
                        ErrorOccurred?.Invoke(this, "No services found on device");
                    }
                }
            }
            catch (DeviceConnectionException ex)
            {
                _connectedDevice = null;
                ErrorOccurred?.Invoke(this, "Connection error: " + ex.Message);
            }
            catch (Exception ex)
            {
                _connectedDevice = null;
                ErrorOccurred?.Invoke(this, "Error: " + ex.Message);
            }
        }

        public async Task DisconnectAsync()
        {
            if (_connectedDevice != null)
            {
                await _adapter.DisconnectDeviceAsync(_connectedDevice);
                _connectedDevice = null;
                _gattService = null;
                _writeCharacteristic = null;
            }
        }    public async Task<bool> SendCommandAsync(string command)
    {
        if (!IsConnected || _writeCharacteristic == null)
        {
            ErrorOccurred?.Invoke(this, "Not connected to a device");
            return false;
        }

        // Don't send duplicate commands to optimize performance
        if (_lastCommand == command)
            return true;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(command);
            await _writeCharacteristic.WriteAsync(bytes);
            _lastCommand = command;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, "Error sending command: " + ex.Message);
            return false;
        }
    }
      // Method specifically for motor commands that bypasses the duplicate check
    public async Task<bool> SendMotorCommandAsync(string command)
    {
        if (!IsConnected || _writeCharacteristic == null)
        {
            ErrorOccurred?.Invoke(this, "Not connected to a device");
            return false;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(command);
            await _writeCharacteristic.WriteAsync(bytes);
            _lastCommand = command; // Still update the last command
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, "Error sending command: " + ex.Message);
            return false;
        }
    }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _adapter.DeviceDiscovered -= OnDeviceDiscovered;
            _adapter.DeviceConnected -= OnDeviceConnected;
            _adapter.DeviceDisconnected -= OnDeviceDisconnected;
            _adapter.DeviceConnectionLost -= OnDeviceConnectionLost;
            
            if (_connectedDevice != null && _adapter.ConnectedDevices.Contains(_connectedDevice))
            {
                _adapter.DisconnectDeviceAsync(_connectedDevice);
            }
        }
    }
}