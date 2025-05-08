using System;
using Android.App;
using Android.OS;
using Android.Widget;
using AndroidApp1.Services;
using System.Threading.Tasks;
using Plugin.BLE;
using System.Threading;

namespace AndroidApp1
{
    [Activity(Label = "@string/app_name")]
    public class RobotControlActivity : Activity
    {        private BluetoothService _bluetoothService = null!;
        private TextView? _tvConnectionStatus;
        private SeekBar? _sbLeftMotor;
        private SeekBar? _sbRightMotor;
        private Button? _btnForward;
        private Button? _btnBackward;
        private Button? _btnLeft;
        private Button? _btnRight;
        private Button? _btnStop;private bool _leftSliderTouched = false;
        private bool _rightSliderTouched = false;
        private int _leftMotorValue = 0;
        private int _rightMotorValue = 0;
        private bool _isUsingButtons = false;
        
        // The slider range is 0-508, with center at 254
        private const int SliderCenter = 254;
        private const int MaxSpeed = 254;
        
        // Timer for continuous command sending
        private Timer? _commandTimer;
        private const int COMMAND_INTERVAL_MS = 500; // Half of the ESP32's timeout (1000ms)
        
        // Timer for continuous slider updates (faster than main timer)
        private Timer? _sliderTimer;
        private const int SLIDER_UPDATE_INTERVAL_MS = 100; // More responsive slider control
          protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the layout resource
            SetContentView(Resource.Layout.activity_robot_control);
              // Get the BluetoothService instance
            _bluetoothService = BluetoothSingleton.Instance ?? new BluetoothService();
            
            if (BluetoothSingleton.Instance == null)
            {
                BluetoothSingleton.Instance = _bluetoothService;
                Toast.MakeText(this, "Created new BluetoothService instance", ToastLength.Short)?.Show();
            }
            else
            {
                Toast.MakeText(this, "Using existing BluetoothService instance", ToastLength.Short)?.Show();
            }

            // Debug connection status
            Toast.MakeText(this, "Connection status: " + (_bluetoothService.IsConnected ? "Connected" : "Disconnected"), ToastLength.Long)?.Show();
            
            // Start the command timer if connected
            if (_bluetoothService.IsConnected)
            {
                StartCommandTimer();
            }

            // Initialize UI elements
            _tvConnectionStatus = FindViewById<TextView>(Resource.Id.tvConnectionStatus);
            _sbLeftMotor = FindViewById<SeekBar>(Resource.Id.sbLeftMotor);
            _sbRightMotor = FindViewById<SeekBar>(Resource.Id.sbRightMotor);
            _btnForward = FindViewById<Button>(Resource.Id.btnForward);
            _btnBackward = FindViewById<Button>(Resource.Id.btnBackward);
            _btnLeft = FindViewById<Button>(Resource.Id.btnLeft);
            _btnRight = FindViewById<Button>(Resource.Id.btnRight);
            _btnStop = FindViewById<Button>(Resource.Id.btnStop);

            // Set up event handlers
            _bluetoothService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _bluetoothService.StatusChanged += OnStatusChanged;
            _bluetoothService.ErrorOccurred += OnErrorOccurred;
            
            // Set initial UI state based on connection
            UpdateConnectionStatus(_bluetoothService.IsConnected);            // Left motor slider events
            _sbLeftMotor?.SetOnSeekBarChangeListener(new SeekBarTouchListener(
                // On Start Touch
                () => 
                {
                    _leftSliderTouched = true;
                    StartSliderTimer();
                },
                // On Stop Touch
                () =>
                {
                    _leftSliderTouched = false;
                    // Reset to center when released
                    if (_sbLeftMotor != null)
                    {
                        _sbLeftMotor.Progress = SliderCenter;
                    }
                    _leftMotorValue = 0;
                    SendMotorValues();
                    
                    // Stop the slider timer if both sliders are released
                    if (!_leftSliderTouched && !_rightSliderTouched)
                    {
                        StopSliderTimer();
                    }
                },
                // On Progress Changed
                (progress, fromUser) =>
                {
                    if (_leftSliderTouched && !_isUsingButtons && fromUser)
                    {
                        // Convert slider value (0-508) to motor value (-254 to 254)
                        _leftMotorValue = progress - SliderCenter;
                    }
                }
            ));

            // Right motor slider events
            _sbRightMotor?.SetOnSeekBarChangeListener(new SeekBarTouchListener(
                // On Start Touch
                () => 
                {
                    _rightSliderTouched = true;
                    StartSliderTimer();
                },
                // On Stop Touch
                () =>
                {
                    _rightSliderTouched = false;
                    // Reset to center when released
                    if (_sbRightMotor != null)
                    {
                        _sbRightMotor.Progress = SliderCenter;
                    }
                    _rightMotorValue = 0;
                    SendMotorValues();
                    
                    // Stop the slider timer if both sliders are released
                    if (!_leftSliderTouched && !_rightSliderTouched)
                    {
                        StopSliderTimer();
                    }
                },
                // On Progress Changed
                (progress, fromUser) =>
                {
                    if (_rightSliderTouched && !_isUsingButtons && fromUser)
                    {
                        // Convert slider value (0-508) to motor value (-254 to 254)
                        _rightMotorValue = progress - SliderCenter;
                    }
                }
            ));            // Button event handlers
            if (_btnForward != null)
                _btnForward.Click += (s, e) =>
                {
                    _isUsingButtons = true;
                    _leftMotorValue = MaxSpeed;
                    _rightMotorValue = MaxSpeed;
                    SendMotorValues();
                };

            if (_btnBackward != null)
                _btnBackward.Click += (s, e) =>
                {
                    _isUsingButtons = true;
                    _leftMotorValue = -MaxSpeed;
                    _rightMotorValue = -MaxSpeed;
                    SendMotorValues();
                };

            if (_btnLeft != null)
                _btnLeft.Click += (s, e) =>
                {
                    _isUsingButtons = true;
                    _leftMotorValue = -MaxSpeed / 2;
                    _rightMotorValue = MaxSpeed / 2;
                    SendMotorValues();
                };

            if (_btnRight != null)
                _btnRight.Click += (s, e) =>
                {
                    _isUsingButtons = true;
                    _leftMotorValue = MaxSpeed / 2;
                    _rightMotorValue = -MaxSpeed / 2;
                    SendMotorValues();
                };

            if (_btnStop != null)
                _btnStop.Click += (s, e) =>
                {
                    _isUsingButtons = false;
                    _leftMotorValue = 0;
                    _rightMotorValue = 0;
                    SendMotorValues();
                    
                    // Reset sliders to center
                    if (_sbLeftMotor != null) _sbLeftMotor.Progress = SliderCenter;
                    if (_sbRightMotor != null) _sbRightMotor.Progress = SliderCenter;
                };            // Initialize sliders to center position
            if (_sbLeftMotor != null) _sbLeftMotor.Progress = SliderCenter;
            if (_sbRightMotor != null) _sbRightMotor.Progress = SliderCenter;
        }
        
        private void OnConnectionStatusChanged(object? sender, bool isConnected)
        {
            RunOnUiThread(() => 
            {
                UpdateConnectionStatus(isConnected);
                
                // Start or stop the command timer based on connection status
                if (isConnected)
                {
                    StartCommandTimer();
                }
                else
                {
                    StopCommandTimer();
                }
            });
        }

        private void UpdateConnectionStatus(bool isConnected)
        {
            if (_tvConnectionStatus != null)
            {
                _tvConnectionStatus.Text = isConnected ? 
                    GetString(Resource.String.connected) : 
                    GetString(Resource.String.disconnected);
            }
            
            // Always enable controls, regardless of connection status, for testing purposes
            if (_sbLeftMotor != null) _sbLeftMotor.Enabled = true;
            if (_sbRightMotor != null) _sbRightMotor.Enabled = true;
            if (_btnForward != null) _btnForward.Enabled = true;
            if (_btnBackward != null) _btnBackward.Enabled = true;
            if (_btnLeft != null) _btnLeft.Enabled = true;
            if (_btnRight != null) _btnRight.Enabled = true;
            if (_btnStop != null) _btnStop.Enabled = true;
        }

        private void OnStatusChanged(object? sender, string status)
        {
            RunOnUiThread(() => Toast.MakeText(this, status, ToastLength.Short)?.Show());
        }        private void OnErrorOccurred(object? sender, string error)
        {
            RunOnUiThread(() => Toast.MakeText(this, error, ToastLength.Long)?.Show());
        }
        
        private async void SendMotorValues()
        {
            await SendMotorValuesAsync();
        }

        private async Task SendMotorValuesAsync()
        {
            // Format: "M <left> <right>"
            string command = $"M {_leftMotorValue} {_rightMotorValue}";
            // Use SendCommandAsync for user initiated commands (which has duplicate checking)
            // Use SendMotorCommandAsync for timer-initiated commands (which bypasses duplicate checking)
            await _bluetoothService.SendMotorCommandAsync(command);
        }

        private void StartCommandTimer()
        {
            StopCommandTimer(); // Stop any existing timer first
            
            _commandTimer = new Timer(OnCommandTimerElapsed, null, 0, COMMAND_INTERVAL_MS);
        }

        private void StopCommandTimer()
        {
            if (_commandTimer != null)
            {
                _commandTimer.Dispose();
                _commandTimer = null;
            }
        }        private void OnCommandTimerElapsed(object? state)
        {
            // Send the current motor values continuously
            // We need to use RunOnUiThread because this is called from a background thread
            RunOnUiThread(async () => await SendMotorValuesAsync());
        }
        
        private void StartSliderTimer()
        {
            StopSliderTimer(); // Stop any existing timer first
            
            _sliderTimer = new Timer(OnSliderTimerElapsed, null, 0, SLIDER_UPDATE_INTERVAL_MS);
        }

        private void StopSliderTimer()
        {
            if (_sliderTimer != null)
            {
                _sliderTimer.Dispose();
                _sliderTimer = null;
            }
        }

        private void OnSliderTimerElapsed(object? state)
        {
            // Only send commands if at least one slider is touched
            if (_leftSliderTouched || _rightSliderTouched)
            {
                // We need to use RunOnUiThread because this is called from a background thread
                RunOnUiThread(async () => await SendMotorValuesAsync());
            }
        }
          protected override void OnDestroy()
        {
            base.OnDestroy();
            
            // Stop all timers
            StopCommandTimer();
            StopSliderTimer();
            
            if (_bluetoothService != null)
            {
                _bluetoothService.ConnectionStatusChanged -= OnConnectionStatusChanged;
                _bluetoothService.StatusChanged -= OnStatusChanged;
                _bluetoothService.ErrorOccurred -= OnErrorOccurred;
            }
        }
        
        protected override void OnPause()
        {
            base.OnPause();
            
            // Stop sending commands when the app is in the background
            StopCommandTimer();
            StopSliderTimer();
            
            // Reset touch states and motor values
            _leftSliderTouched = false;
            _rightSliderTouched = false;
            _leftMotorValue = 0;
            _rightMotorValue = 0;
            
            // Send a final stop command
            SendMotorValues();
        }
        
        protected override void OnResume()
        {
            base.OnResume();
            
            // Resume sending commands when the app comes back to the foreground
            if (_bluetoothService?.IsConnected == true)
            {
                StartCommandTimer();
            }
            
            // Note: Slider timer will start automatically when sliders are touched
        }
          // A singleton to keep the Bluetooth service instance across activities
        public static class BluetoothSingleton
        {
            public static BluetoothService? Instance { get; set; }
        }
          // Custom SeekBar listener to handle touch events
        private class SeekBarTouchListener : Java.Lang.Object, SeekBar.IOnSeekBarChangeListener
        {
            private readonly Action _onStartTrackingTouch;
            private readonly Action _onStopTrackingTouch;
            private readonly Action<int, bool> _onProgressChanged;
            
            public SeekBarTouchListener(Action onStartTrackingTouch, Action onStopTrackingTouch, Action<int, bool>? onProgressChanged = null)
            {
                _onStartTrackingTouch = onStartTrackingTouch;
                _onStopTrackingTouch = onStopTrackingTouch;
                _onProgressChanged = onProgressChanged ?? ((progress, fromUser) => {});
            }
            
            public void OnProgressChanged(SeekBar? seekBar, int progress, bool fromUser)
            {
                _onProgressChanged?.Invoke(progress, fromUser);
            }
            
            public void OnStartTrackingTouch(SeekBar? seekBar)
            {
                _onStartTrackingTouch?.Invoke();
            }
            
            public void OnStopTrackingTouch(SeekBar? seekBar)
            {
                _onStopTrackingTouch?.Invoke();
            }
        }
    }
}