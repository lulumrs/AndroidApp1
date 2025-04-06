2️⃣ Bluetooth Connection Page

    Display a list of available Bluetooth devices.

    Allow the user to scan and select an ESP32 device.

    Implement a "Connect" button to establish the connection.

    Redirect to the control interface once connected.

3️⃣ Control Interface (Sliders & Buttons)

    Sliders (One on each side):

        Range: -254 to 254.

        Automatically return to 0 when released.

        Send values continuously to control each motor.

    Control Buttons (Centered):

        Four buttons: Forward, Backward, Left, Right.

        Send predefined commands when pressed.

        Commands override slider values if used.

    Ensure the layout adapts to portrait and landscape modes.

4️⃣ Communication with ESP32 via Bluetooth

    Use a specific data format to send commands (e.g., "M <left> <right>").

    Transmit motor values only when changed to optimize performance.

5️⃣ ESP32 Firmware (Arduino) in a separated .ino file

    Initialize Bluetooth communication with a recognizable device name.

    Continuously listen for commands from the app.

    Parse received data and control the motors accordingly:

        Adjust PWM values for speed and direction.

        Interpret button commands for basic movements.

    Use analogWrite

    H-Bridge with enable pins

    Implement a failsafe mechanism (e.g., stop motors if no command received for a certain time).