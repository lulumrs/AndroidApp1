using System;
using Android.App;
using Android.OS;
using Android.Widget;
using AndroidApp1.Services;
using System.Threading.Tasks;
using Plugin.BLE;

namespace AndroidApp1
{
    [Activity(Label = "@string/app_name")]
    public class RobotControlActivity : Activity
    {
        private BluetoothService _bluetoothService;
        private TextView _tvConnectionStatus;
        private SeekBar _sbLeftMotor;
        private SeekBar _sbRightMotor;
        private Button _btnForward;
        private Button _btnBackward;
        private Button _btnLeft;
        private Button _btnRight;
        private Button _btnStop;
        
        private bool _sliderBeingTouched = false;
        private int _leftMotorValue = 0;
        private int _rightMotorValue = 0;
        private bool _isUsingButtons = false;
        
        // The slider range is 0-508, with center at 254
        private const int SliderCenter = 254;
        private const int MaxSpeed = 254;

        protected override async void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the layout resource
            SetContentView(Resource.Layout.activity_robot_control);
            
            // Get the BluetoothService instance
            _bluetoothService = BluetoothSingleton.Instance;
            if (_bluetoothService == null)
            {
                _bluetoothService = new BluetoothService();
                BluetoothSingleton.Instance = _bluetoothService;
                Toast.MakeText(this, "Created new BluetoothService instance", ToastLength.Short)?.Show();
            }
            else
            {
                Toast.MakeText(this, "Using existing BluetoothService instance", ToastLength.Short)?.Show();
            }

            // Debug connection status
            Toast.MakeText(this, "Connection status: " + (_bluetoothService.IsConnected ? "Connected" : "Disconnected"), ToastLength.Long)?.Show();

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
            UpdateConnectionStatus(_bluetoothService.IsConnected);

            // Left motor slider events
            _sbLeftMotor.ProgressChanged += (sender, e) =>
            {
                if (_sliderBeingTouched && !_isUsingButtons)
                {
                    // Convert slider value (0-508) to motor value (-254 to 254)
                    _leftMotorValue = e.Progress - SliderCenter;
                    SendMotorValues();
                }
            };
            
            _sbLeftMotor.SetOnSeekBarChangeListener(new SeekBarTouchListener(() => 
            {
                _sliderBeingTouched = true;
            }, () =>
            {
                _sliderBeingTouched = false;
                // Reset to center when released
                _sbLeftMotor.Progress = SliderCenter;
                _leftMotorValue = 0;
                SendMotorValues();
            }));

            // Right motor slider events
            _sbRightMotor.ProgressChanged += (sender, e) =>
            {
                if (_sliderBeingTouched && !_isUsingButtons)
                {
                    // Convert slider value (0-508) to motor value (-254 to 254)
                    _rightMotorValue = e.Progress - SliderCenter;
                    SendMotorValues();
                }
            };
            
            _sbRightMotor.SetOnSeekBarChangeListener(new SeekBarTouchListener(() => 
            {
                _sliderBeingTouched = true;
            }, () =>
            {
                _sliderBeingTouched = false;
                // Reset to center when released
                _sbRightMotor.Progress = SliderCenter;
                _rightMotorValue = 0;
                SendMotorValues();
            }));

            // Button event handlers
            _btnForward.Click += (s, e) =>
            {
                _isUsingButtons = true;
                _leftMotorValue = MaxSpeed;
                _rightMotorValue = MaxSpeed;
                SendMotorValues();
            };

            _btnBackward.Click += (s, e) =>
            {
                _isUsingButtons = true;
                _leftMotorValue = -MaxSpeed;
                _rightMotorValue = -MaxSpeed;
                SendMotorValues();
            };

            _btnLeft.Click += (s, e) =>
            {
                _isUsingButtons = true;
                _leftMotorValue = -MaxSpeed / 2;
                _rightMotorValue = MaxSpeed / 2;
                SendMotorValues();
            };

            _btnRight.Click += (s, e) =>
            {
                _isUsingButtons = true;
                _leftMotorValue = MaxSpeed / 2;
                _rightMotorValue = -MaxSpeed / 2;
                SendMotorValues();
            };

            _btnStop.Click += (s, e) =>
            {
                _isUsingButtons = false;
                _leftMotorValue = 0;
                _rightMotorValue = 0;
                SendMotorValues();
                
                // Reset sliders to center
                _sbLeftMotor.Progress = SliderCenter;
                _sbRightMotor.Progress = SliderCenter;
            };

            // Initialize sliders to center position
            _sbLeftMotor.Progress = SliderCenter;
            _sbRightMotor.Progress = SliderCenter;
        }

        private void OnConnectionStatusChanged(object? sender, bool isConnected)
        {
            RunOnUiThread(() => UpdateConnectionStatus(isConnected));
        }

        private void UpdateConnectionStatus(bool isConnected)
        {
            _tvConnectionStatus.Text = isConnected ? 
                GetString(Resource.String.connected) : 
                GetString(Resource.String.disconnected);
            
            // Always enable controls, regardless of connection status, for testing purposes
            _sbLeftMotor.Enabled = true;
            _sbRightMotor.Enabled = true;
            _btnForward.Enabled = true;
            _btnBackward.Enabled = true;
            _btnLeft.Enabled = true;
            _btnRight.Enabled = true;
            _btnStop.Enabled = true;
        }

        private void OnStatusChanged(object? sender, string status)
        {
            RunOnUiThread(() => Toast.MakeText(this, status, ToastLength.Short)?.Show());
        }

        private void OnErrorOccurred(object? sender, string error)
        {
            RunOnUiThread(() => Toast.MakeText(this, error, ToastLength.Long)?.Show());
        }

        private async void SendMotorValues()
        {
            // Format: "M <left> <right>"
            string command = $"M {_leftMotorValue} {_rightMotorValue}";
            await _bluetoothService.SendCommandAsync(command);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            
            if (_bluetoothService != null)
            {
                _bluetoothService.ConnectionStatusChanged -= OnConnectionStatusChanged;
                _bluetoothService.StatusChanged -= OnStatusChanged;
                _bluetoothService.ErrorOccurred -= OnErrorOccurred;
            }
        }
        
        // A singleton to keep the Bluetooth service instance across activities
        public static class BluetoothSingleton
        {
            public static BluetoothService Instance { get; set; }
        }
        
        // Custom SeekBar listener to handle touch events
        private class SeekBarTouchListener : Java.Lang.Object, SeekBar.IOnSeekBarChangeListener
        {
            private readonly Action _onStartTrackingTouch;
            private readonly Action _onStopTrackingTouch;
            
            public SeekBarTouchListener(Action onStartTrackingTouch, Action onStopTrackingTouch)
            {
                _onStartTrackingTouch = onStartTrackingTouch;
                _onStopTrackingTouch = onStopTrackingTouch;
            }
            
            public void OnProgressChanged(SeekBar seekBar, int progress, bool fromUser) { }
            
            public void OnStartTrackingTouch(SeekBar seekBar)
            {
                _onStartTrackingTouch?.Invoke();
            }
            
            public void OnStopTrackingTouch(SeekBar seekBar)
            {
                _onStopTrackingTouch?.Invoke();
            }
        }
    }
}