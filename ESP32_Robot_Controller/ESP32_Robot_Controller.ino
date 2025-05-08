#include <BLEDevice.h>
#include <BLEUtils.h>
#include <BLEServer.h>
#include <BLE2902.h>

// Define pin constants for motor control
#define MOTOR_LEFT_EN    5  // Enable Pin for left motor
#define MOTOR_LEFT_IN1   0  // Input 1 for left motor
#define MOTOR_LEFT_IN2   18  // Input 2 for left motor
#define MOTOR_RIGHT_EN   2  // Enable Pin for right motor
#define MOTOR_RIGHT_IN1  23  // Input 3 for right motor
#define MOTOR_RIGHT_IN2  19  // Input 4 for right motor

// BLE UUIDs
#define SERVICE_UUID        "4FAFC201-1FB5-459E-8FCC-C5C9C331914B"
#define CHARACTERISTIC_UUID "BEB5483E-36E1-4688-B7F5-EA07361B26A8"

// Motor control variables
int leftMotorSpeed = 0;   // -254 to 254
int rightMotorSpeed = 0;  // -254 to 254

// Timeout variables for failsafe
unsigned long lastCommandTime = 0;
const unsigned long TIMEOUT_MS = 1000; // Stop motors if no command for 1 second

// BLE variables
BLEServer* pServer = NULL;
BLECharacteristic* pCharacteristic = NULL;
bool deviceConnected = false;
// Update motor speeds and directions
void updateMotors() {
  // Left motor control
  if (leftMotorSpeed > 0) {
    // Forward
    digitalWrite(MOTOR_LEFT_IN1, HIGH);
    digitalWrite(MOTOR_LEFT_IN2, LOW);
    analogWrite(MOTOR_LEFT_EN, leftMotorSpeed);
  } else if (leftMotorSpeed < 0) {
    // Backward
    digitalWrite(MOTOR_LEFT_IN1, LOW);
    digitalWrite(MOTOR_LEFT_IN2, HIGH);
    analogWrite(MOTOR_LEFT_EN, abs(leftMotorSpeed));
  } else {
    // Stop
    digitalWrite(MOTOR_LEFT_IN1, LOW);
    digitalWrite(MOTOR_LEFT_IN2, LOW);
    analogWrite(MOTOR_LEFT_EN, 0);
  }
  
  // Right motor control
  if (rightMotorSpeed > 0) {
    // Forward
    digitalWrite(MOTOR_RIGHT_IN1, HIGH);
    digitalWrite(MOTOR_RIGHT_IN2, LOW);
    analogWrite(MOTOR_RIGHT_EN, rightMotorSpeed);
  } else if (rightMotorSpeed < 0) {
    // Backward
    digitalWrite(MOTOR_RIGHT_IN1, LOW);
    digitalWrite(MOTOR_RIGHT_IN2, HIGH);
    analogWrite(MOTOR_RIGHT_EN, abs(rightMotorSpeed));
  } else {
    // Stop
    digitalWrite(MOTOR_RIGHT_IN1, LOW);
    digitalWrite(MOTOR_RIGHT_IN2, LOW);
    analogWrite(MOTOR_RIGHT_EN, 0);
  }
}

// BLE Server callbacks
class MyServerCallbacks: public BLEServerCallbacks {
  void onConnect(BLEServer* pServer) {
    deviceConnected = true;
    Serial.println("Device connected");
  }

  void onDisconnect(BLEServer* pServer) {
    deviceConnected = false;
    Serial.println("Device disconnected");
    
    // Stop motors when disconnected for safety
    leftMotorSpeed = 0;
    rightMotorSpeed = 0;
    updateMotors();
    
    // Restart advertising to be connectable again
    pServer->getAdvertising()->start();
  }
};

// Characteristic callbacks to handle incoming data
class MyCharacteristicCallbacks: public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic* pCharacteristic) {
    // Fix the type conversion issue
    String valueStr = pCharacteristic->getValue();
    std::string value(valueStr.c_str());
    
    if (value.length() > 0) {
      lastCommandTime = millis(); // Reset timeout
      Serial.print("Received command: ");
      Serial.println(value.c_str());
      
      // Parse the incoming command
      parseCommand(value);
    }
  }
  
  // Parse command string in format "M <left> <right>"
  void parseCommand(std::string command) {
    if (command.length() < 2) return;
    
    // Check if it's a motor command
    if (command[0] == 'M' && command[1] == ' ') {
      int leftVal = 0;
      int rightVal = 0;
      
      // Extract the two numbers
      sscanf(command.c_str(), "M %d %d", &leftVal, &rightVal);
      
      // Constrain values to valid range
      leftMotorSpeed = constrain(leftVal, -254, 254);
      rightMotorSpeed = constrain(rightVal, -254, 254);
      
      Serial.print("Left: ");
      Serial.print(leftMotorSpeed);
      Serial.print(" Right: ");
      Serial.println(rightMotorSpeed);
      
      // Update motors with new values
      updateMotors();
    }
  }
};

void setup() {
  Serial.begin(115200);
  Serial.println("Starting ESP32 Robot Controller");
  
  // Configure motor control pins
  pinMode(MOTOR_LEFT_EN, OUTPUT);
  pinMode(MOTOR_LEFT_IN1, OUTPUT);
  pinMode(MOTOR_LEFT_IN2, OUTPUT);
  pinMode(MOTOR_RIGHT_EN, OUTPUT);
  pinMode(MOTOR_RIGHT_IN1, OUTPUT);
  pinMode(MOTOR_RIGHT_IN2, OUTPUT);
  
  // Set initial motor state (stopped)
  digitalWrite(MOTOR_LEFT_IN1, LOW);
  digitalWrite(MOTOR_LEFT_IN2, LOW);
  digitalWrite(MOTOR_RIGHT_IN1, LOW);
  digitalWrite(MOTOR_RIGHT_IN2, LOW);
  analogWrite(MOTOR_LEFT_EN, 0);
  analogWrite(MOTOR_RIGHT_EN, 0);
  
  // Initialize BLE
  BLEDevice::init("ESP32-Robot");
  
  // Create the BLE Server
  pServer = BLEDevice::createServer();
  pServer->setCallbacks(new MyServerCallbacks());
  
  // Create the BLE Service
  BLEService *pService = pServer->createService(SERVICE_UUID);
  
  // Create the BLE Characteristic
  pCharacteristic = pService->createCharacteristic(
                      CHARACTERISTIC_UUID,
                      BLECharacteristic::PROPERTY_READ   |
                      BLECharacteristic::PROPERTY_WRITE  |
                      BLECharacteristic::PROPERTY_NOTIFY |
                      BLECharacteristic::PROPERTY_INDICATE
                    );
  
  pCharacteristic->setCallbacks(new MyCharacteristicCallbacks());
  pCharacteristic->addDescriptor(new BLE2902());
  
  // Start the service
  pService->start();
  
  // Start advertising
  BLEAdvertising *pAdvertising = BLEDevice::getAdvertising();
  pAdvertising->addServiceUUID(SERVICE_UUID);
  pAdvertising->setScanResponse(true);
  pAdvertising->setMinPreferred(0x06);  // Makes connecting more reliable on iOS
  pAdvertising->setMaxPreferred(0x12);  // Fixed from duplicate setMinPreferred
  BLEDevice::startAdvertising();
  
  Serial.println("BLE server ready, waiting for connections...");
}

void loop() {
  // Check for timeout (failsafe)
  if (millis() - lastCommandTime > TIMEOUT_MS && (leftMotorSpeed != 0 || rightMotorSpeed != 0)) {
    Serial.println("Command timeout - stopping motors");
    leftMotorSpeed = 0;
    rightMotorSpeed = 0;
    updateMotors();
  }
  
  delay(20); // Small delay to prevent CPU hogging
}
