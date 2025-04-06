using Android.App;
using Android.OS;
using Android.Content;

namespace AndroidApp1
{
    [Activity(Label = "@string/app_name", MainLauncher = true)]
    public class MainActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Redirect to the Bluetooth Connection Activity
            var intent = new Intent(this, typeof(BluetoothConnectionActivity));
            StartActivity(intent);
            Finish(); // Close this activity
        }
    }
}