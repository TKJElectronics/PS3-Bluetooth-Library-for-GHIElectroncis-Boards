/*
This code was developed by Kristian Lauszus. 
For more information visit his blog at: http://blog.tkjelectronics.dk/2011/09/fez-panda-ps3-controller-via-bluetooth/ or send him an email at kristianl@tkjelectronics.dk
You should also visit the offical wiki: http://wiki.tinyclr.com/index.php?title=PS3_Controller

A special thanks go to the following people:
    "Richard Ibbotson" who made this guide: http://www.circuitsathome.com/mcu/ps3-and-wiimote-game-controllers-on-the-arduino-host-shield-part-1
     It inspired me to get starting and had a lot of good information for the USB communication 
 
    "Tomoyuki Tanaka" for releasing his code for the Arduino USB Host shield connected to the wiimote: http://www.circuitsathome.com/mcu/rc-car-controlled-by-wii-remote-on-arduino
    It helped me a lot to see the structure of the bluetooth communication

The code is released under the GNU General Public License
*/
using System.Text;
using System.IO.Ports;
using System.Threading;
using Microsoft.SPOT;
using GHIElectronics.NETMF.USBHost;

using PS3ControllerUSBNavigation;
using PS3ControllerUSBMove;
using PS3ControllerUSB;
using PS3ControllerBluetooth;

namespace PS3_Controller
{
    public class Program
    {       
        //PS3 Controllers
        const byte PS3Max = 4;//Max 4 controllers - this can be set to whatever you like
        static PS3Controller[] PS3 = new PS3Controller[PS3Max];
        static USBH_Device[] PS3Device = new USBH_Device[PS3Max];//Used to store the devices - see "DeviceDisconnectedEvent"                
        static byte PS3Connected;//Holds the numbers of controllers current connected

        //Bluetooth
        static Bluetooth BT;

        //Handlers for Move and Navigation controller
        static PS3Move Move;
        static PS3Navigation Navigation;        

        //Setup serial connection
        static SerialPort UART = new SerialPort("COM2", 115200);

        //Thread handlers
        static Thread PS3Handler = new Thread(PS3Thread);//Main Thread     
        static Thread RumbleHandler = new Thread(Rumble);//Handles the rumble function
        static Thread RumbleHandlerBT = new Thread(RumbleBT);//Handles the rumble function (Bluetooth)

        //Variables
        static bool[] runThread = new bool[PS3Max];//Set if loop should be running or not        
        static bool[] PS3Rumble = new bool[PS3Max];//Start rumble
        static bool PS3RumbleBT;//Start rumble (Bluetooth)
        static bool[] print4DOF = new bool[PS3Max];//Print angles and gyro value
        static bool print4DOFBT;//Print angles and gyro values (Bluetooth)

        static bool print9DOFBT;//Print accelerometer, gyro and magnetometer data from the motion controller
        
        public static void Main()
        {
            UART.Open();//Start serial communication on COM2
            WriteSerial("\r\nInitialized");//Indicate that it's up and running

            //Start USB Host events
            USBHostController.DeviceConnectedEvent += DeviceConnectedEvent;
            USBHostController.DeviceDisconnectedEvent += DeviceDisconnectedEvent;

            //Start threads            
            PS3Handler.Start();
            RumbleHandler.Start();
            RumbleHandlerBT.Start();

            //Sleep forever
            Thread.Sleep(Timeout.Infinite);
        }
        static void DeviceConnectedEvent(USBH_Device device)
        {            
            //Check if device is a PS3 Controller
            if (device.VENDOR_ID != PS3Controller.PS3_VENDOR_ID || device.PRODUCT_ID != PS3Controller.PS3_PRODUCT_ID)
            {
                if (device.TYPE == USBH_DeviceType.Hub)
                    WriteSerial("Hub Connected");
                else if (device.PRODUCT_ID == Bluetooth.CSR_PRODUCT_ID && device.VENDOR_ID == Bluetooth.CSR_VENDOR_ID)//Check if it is the bluetooth dongle
                {
                    WriteSerial("Bluetooth Dongle Connected");
                    BT = new Bluetooth(device);
                }
                else if (device.VENDOR_ID == PS3Move.PS3MOVE_VENDOR_ID && device.PRODUCT_ID == PS3Move.PS3MOVE_PRODUCT_ID)
                {
                    WriteSerial("PS3 Move Controller Connected");
                    Move = new PS3Move(device);
                }
                else if (device.VENDOR_ID == PS3Navigation.PS3NAVIGATION_VENDOR_ID && device.PRODUCT_ID == PS3Navigation.PS3NAVIGATION_PRODUCT_ID)
                {
                    WriteSerial("PS3 Navigation Controller Connected");
                    Navigation = new PS3Navigation(device);                    
                }
                else
                    WriteSerial("Unknown Device Connected");
                return;
            }
            if (PS3Connected == PS3Max)//Check if the maximum number of controller is exceeded
            {
                WriteSerial(PS3Max + " Controllers are already connected");
                return;
            }               

            PS3[PS3Connected] = new PS3Controller(device);//Connect the PS3 Controller
            PS3Device[PS3Connected] = device;//Store the device see "DeviceDisconnectedEvent"
            PS3SetLED(PS3Connected);//Set the specific LED on
            runThread[PS3Connected] = true;//Start the loop            
            WriteSerial("PS3 Controller: " + (PS3Connected + 1) + " - Connected");          

            PS3Connected++;                               
        }
        static void DeviceDisconnectedEvent(USBH_Device device)
        {
            //Check if device is a PS3 Controller
            if (device.VENDOR_ID != PS3Controller.PS3_VENDOR_ID || device.PRODUCT_ID != PS3Controller.PS3_PRODUCT_ID)
            {                
                if (device.TYPE == USBH_DeviceType.Hub)
                {
                    WriteSerial("Hub Disconnected");
                    for (byte i = 0; i < PS3Connected; i++)//All controllers has to be stopped
                    {
                        runThread[i] = false;
                        PS3[i].Abort();
                        BT.Abort();
                    }
                }
                else if (device.PRODUCT_ID == Bluetooth.CSR_PRODUCT_ID && device.VENDOR_ID == Bluetooth.CSR_VENDOR_ID)
                {
                    WriteSerial("Bluetooth Dongle Disconnected");
                    PS3RumbleBT = false;
                    BT.Abort();
                }
                else if (device.VENDOR_ID == PS3Move.PS3MOVE_VENDOR_ID && device.PRODUCT_ID == PS3Move.PS3MOVE_PRODUCT_ID)
                {
                    WriteSerial("PS3 Move Controller Disconnected");
                    Move.Abort();
                }
                else if (device.VENDOR_ID == PS3Navigation.PS3NAVIGATION_VENDOR_ID && device.PRODUCT_ID == PS3Navigation.PS3NAVIGATION_PRODUCT_ID)
                {
                    WriteSerial("PS3 Navigation Disconnected");
                    Navigation.Abort();                    
                }
                else
                    WriteSerial("Unknown Device Disconnected");
                return;
            }

            PS3Connected--;

            if (device.ID == PS3Device[PS3Connected].ID)//Check if it is the last one
            {                
                runThread[PS3Connected] = false;//Stop the loop for the last controller
                PS3Rumble[PS3Connected] = false;//Also stop the rumble thread if active
                PS3[PS3Connected].Abort();//Abort the reading thread
                WriteSerial("PS3 Controller: " + (PS3Connected + 1) + " - Disconnected");
            }
            else
            {                
                byte PS3number;
                for (PS3number = 0; PS3number < PS3Connected; PS3number++)//Check which number that was disconnected
                    if (device.ID == PS3Device[PS3number].ID)
                        break;

                runThread[PS3number] = false;
                PS3Rumble[PS3number] = false;//Also stop the rumble thread if active
                PS3[PS3number].Abort();
                WriteSerial("PS3 Controller: " + (PS3number + 1) + " - Disconnected");

                //Move all the controllers from that point one down
                for (byte i = PS3number; i < PS3Connected; i++)//Does not include the last one, as it is allways moved one down
                {                    
                    //Stop that thread including the next one
                    runThread[i] = false;
                    PS3[i].Abort();
                    runThread[i + 1] = false;
                    PS3[i + 1].Abort();

                    //Move one down
                    PS3[i] = new PS3Controller(PS3Device[i + 1]);
                    PS3Device[i] = PS3Device[i + 1];
                    PS3SetLED(i);

                    runThread[i] = true;
                    WriteSerial("PS3 Controller: " + (i + 2) + " - Changed to " + (i + 1));
                }             
            }
        }
        public static void PS3Thread()
        {
            string[] output = new string[PS3Max];
            string outputNavigattion;
            string outputBT = "";
            string[] lastoutput = new string[PS3Max];
            string lastoutputNavigattion = "";
            string lastoutputBT = "";
            bool moveRumbleOn = false;//Indicate that rumble is on
            while (true)
            {
                if (Bluetooth.PS3MoveBTConnected)
                {
                    outputBT = "";//Reset output
                    if (BT.GetButton(Bluetooth.Button.SELECT_MOVE))
                    {
                        outputBT += " - Select";
                        print9DOFBT = false;
                    }
                    if (BT.GetButton(Bluetooth.Button.START_MOVE))
                    {
                        outputBT += " - Start";
                        print9DOFBT = true;
                    }

                    if (BT.GetButton(Bluetooth.Button.TRIANGLE_MOVE))
                    {
                        BT.hid_MoveSetLed(Bluetooth.Colors.Red);
                        outputBT += " - Triangle";
                    }
                    if (BT.GetButton(Bluetooth.Button.CIRCLE_MOVE))
                    {
                        BT.hid_MoveSetLed(Bluetooth.Colors.Green);
                        outputBT += " - Circle";
                    }
                    if (BT.GetButton(Bluetooth.Button.SQUARE_MOVE))
                    {
                        BT.hid_MoveSetLed(Bluetooth.Colors.Blue);
                        outputBT += " - Square";                        
                    }
                    if (BT.GetButton(Bluetooth.Button.CROSS_MOVE))
                    {
                        BT.hid_MoveSetLed(Bluetooth.Colors.Yellow);
                        outputBT += " - Cross";
                    }

                    if (BT.GetButton(Bluetooth.Button.PS_MOVE))
                    {
                        BT.disconnectController();//Disconnect the controller                        
                        outputBT += " - PS";
                    }
                    if (BT.GetButton(Bluetooth.Button.MOVE_MOVE))
                    {                        
                        BT.hid_MoveSetLed(Bluetooth.Colors.Off);                        
                        outputBT += " - Move";
                        outputBT += " - " + BT.GetStatusString();//Print status string
                    }
                    if (BT.GetButton(Bluetooth.Button.T_MOVE))
                    {
                        BT.hid_MoveSetRumble(BT.GetAnalogButton(Bluetooth.AnalogButton.T_MOVE));
                        outputBT += " - T: " + BT.GetAnalogButton(Bluetooth.AnalogButton.T_MOVE);
                        moveRumbleOn = true;
                    }
                    else if (moveRumbleOn)
                    {
                        BT.hid_MoveSetRumble(0);
                        moveRumbleOn = false;
                    }
                    if (print9DOFBT)//Print data
                    {                        
                        //outputBT += " - aX: " + BT.GetSensor(Bluetooth.Sensor.aXmove) + " - aY: " + BT.GetSensor(Bluetooth.Sensor.aYmove) + " aZ: " + BT.GetSensor(Bluetooth.Sensor.aZmove);
                        //outputBT += " - gX: " + BT.GetSensor(Bluetooth.Sensor.gXmove) + " - gY: " + BT.GetSensor(Bluetooth.Sensor.gYmove) + " gZ: " + BT.GetSensor(Bluetooth.Sensor.gZmove);
                        //outputBT += " - mX: " + BT.GetSensor(Bluetooth.Sensor.mXmove) + " - mY: " + BT.GetSensor(Bluetooth.Sensor.mYmove) + " mZ: " + BT.GetSensor(Bluetooth.Sensor.mZmove);
                        
                        string input;
                        string templow;
                        string temphigh;
                          
                        input = BT.GetSensor(Bluetooth.Sensor.tempMove).ToString();
                        if (input.Length > 3)
                        {
                            temphigh = input.Substring(0, 2);
                            templow = input.Substring(2, 2);
                        }
                        else
                        {
                            temphigh = input.Substring(0, 1);
                            templow = input.Substring(1, 2);
                        }
                        outputBT += " - Temperature: " + temphigh + "." + templow;
                        
                        //outputBT += " - Temperature: " + BT.GetSensor(Bluetooth.Sensor.tempMove);                        
                    }                    
                    
                    if (outputBT != "" && outputBT != lastoutputBT)//Check if output is not empty and not equal to the last one
                        WriteSerial("PS3 Move Controller" + outputBT);
                    lastoutputBT = outputBT;
                }

                if(Bluetooth.PS3NavigationBTConnected)//All the bytes for the buttons is at the place as the original Dualshock controller
                {
                    outputBT = "";//Reset output
                    if (BT.GetButton(Bluetooth.Button.CIRCLE))
                        outputBT += " - Circle";
                    if (BT.GetButton(Bluetooth.Button.CROSS))
                        outputBT += " - Cross";

                    if (BT.GetButton(Bluetooth.Button.UP))
                    {
                        outputBT += " - Up";
                        outputBT += " - " + BT.GetStatusString();//Print status string
                    }
                    if (BT.GetButton(Bluetooth.Button.RIGHT))
                        outputBT += " - Right";
                    if (BT.GetButton(Bluetooth.Button.DOWN))                    
                        outputBT += " - Down";                    
                    if (BT.GetButton(Bluetooth.Button.LEFT))
                        outputBT += " - Left";

                    if (BT.GetButton(Bluetooth.Button.L1))
                        outputBT += " - L1: " + BT.GetAnalogButton(Bluetooth.AnalogButton.L1);//Include analog readings
                    if (BT.GetButton(Bluetooth.Button.L2))
                        outputBT += " - L2: " + BT.GetAnalogButton(Bluetooth.AnalogButton.L2);//Include analog readings
                    if (BT.GetButton(Bluetooth.Button.L3))
                        outputBT += " - L3";

                    if (BT.GetButton(Bluetooth.Button.PS))
                    {                        
                        BT.disconnectController();//Disconnect the controller
                        outputBT += " - PS";
                    }

                    //Ignore all joystick values if too small - 127 is center
                    if (BT.GetAnalogHat(Bluetooth.AnalogHat.LeftHatX) > 137 || BT.GetAnalogHat(Bluetooth.AnalogHat.LeftHatX) < 117)
                        outputBT += " - LeftHatX: " + BT.GetAnalogHat(Bluetooth.AnalogHat.LeftHatX);
                    if (BT.GetAnalogHat(Bluetooth.AnalogHat.LeftHatY) > 137 || BT.GetAnalogHat(Bluetooth.AnalogHat.LeftHatY) < 117)
                        outputBT += " - LeftHatY: " + BT.GetAnalogHat(Bluetooth.AnalogHat.LeftHatY);

                    if (outputBT != "" && outputBT != lastoutputBT)//Check if output is not empty and not equal to the last one
                        WriteSerial("PS3 Navigation Controller" + outputBT);
                    lastoutputBT = outputBT;
                }
                if (PS3Navigation.PS3NavigationConnected) 
                {
                    outputNavigattion = "";//Reset output
                    if (Navigation.GetButton(PS3Navigation.Button.CIRCLE))
                        outputNavigattion += " - Circle";

                    if (Navigation.GetButton(PS3Navigation.Button.CROSS))
                        outputNavigattion += " - Cross";

                    if (Navigation.GetButton(PS3Navigation.Button.UP))
                    {
                        outputNavigattion += " - Up";
                        outputNavigattion += " - " + Navigation.GetStatusString();//Print status string                       
                    }
                    if (Navigation.GetButton(PS3Navigation.Button.RIGHT))
                    {
                        outputNavigattion += " - Right";
                        Navigation.SetBD_Addr(Bluetooth.BDaddr);//Set the bluetooth address into the controller
                        outputNavigattion += " - Set BT Address: " + ByteToHex(Bluetooth.BDaddr[0]) + " " + ByteToHex(Bluetooth.BDaddr[1]) + " " + ByteToHex(Bluetooth.BDaddr[2]) + " " + ByteToHex(Bluetooth.BDaddr[3]) + " " + ByteToHex(Bluetooth.BDaddr[4]) + " " + ByteToHex(Bluetooth.BDaddr[5]);
                    }
                    if (Navigation.GetButton(PS3Navigation.Button.DOWN))
                        outputNavigattion += " - Down";

                    if (Navigation.GetButton(PS3Navigation.Button.LEFT))
                    {
                        outputNavigattion += " - Left";
                        byte[] buffer = Navigation.GetBD_Addr();//Read the bluetooth address in the controller
                        outputNavigattion += " - Got BT Address: " + ByteToHex(buffer[0]) + " " + ByteToHex(buffer[1]) + " " + ByteToHex(buffer[2]) + " " + ByteToHex(buffer[3]) + " " + ByteToHex(buffer[4]) + " " + ByteToHex(buffer[5]);
                    }

                    if (Navigation.GetButton(PS3Navigation.Button.L1))
                        outputNavigattion += " - L1";
                    if (Navigation.GetButton(PS3Navigation.Button.L2))
                        outputNavigattion += " - L2: " + Navigation.GetAnalogButton(PS3Navigation.AnalogButton.L2);//Include analog readings - this can be done for almost all buttons
                    if (Navigation.GetButton(PS3Navigation.Button.L3))
                        outputNavigattion += " - L3";

                    if (Navigation.GetButton(PS3Navigation.Button.PS))
                        outputNavigattion += " - PS";

                    //Ignore all joystick values if too small - 127 is center
                    if (Navigation.GetAnalogHat(PS3Navigation.AnalogHat.LeftHatX) > 137 || Navigation.GetAnalogHat(PS3Navigation.AnalogHat.LeftHatX) < 117)
                        outputNavigattion += " - LeftHatX: " + Navigation.GetAnalogHat(PS3Navigation.AnalogHat.LeftHatX);
                    if (Navigation.GetAnalogHat(PS3Navigation.AnalogHat.LeftHatY) > 137 || Navigation.GetAnalogHat(PS3Navigation.AnalogHat.LeftHatY) < 117)
                        outputNavigattion += " - LeftHatY: " + Navigation.GetAnalogHat(PS3Navigation.AnalogHat.LeftHatY);

                    if (outputNavigattion != "" && outputNavigattion != lastoutputNavigattion)//Check if output is not empty and not equal to the last one
                        WriteSerial("PS3 Controller" + outputNavigattion);
                    lastoutputNavigattion = outputNavigattion;
                }
                if (Bluetooth.PS3BTConnected)
                {
                    outputBT = "";//Reset output
                    if (BT.GetButton(Bluetooth.Button.TRIANGLE))
                        outputBT += " - Triangle";
                    if (BT.GetButton(Bluetooth.Button.CIRCLE))
                        outputBT += " - Circle";
                    if (BT.GetButton(Bluetooth.Button.CROSS))
                        outputBT += " - Cross";
                    if (BT.GetButton(Bluetooth.Button.SQUARE))
                        outputBT += " - Square";

                    if (BT.GetButton(Bluetooth.Button.UP))
                    {
                        outputBT += " - Up";
                        print4DOFBT = true;//Start printing angles and gyro values
                    }
                    if (BT.GetButton(Bluetooth.Button.RIGHT))
                        outputBT += " - Right";

                    if (BT.GetButton(Bluetooth.Button.DOWN))
                    {
                        outputBT += " - Down";
                        print4DOFBT = false;//Stop printing angles and gyro values
                    }
                    if (BT.GetButton(Bluetooth.Button.LEFT))
                        outputBT += " - Left";

                    if (BT.GetButton(Bluetooth.Button.L1))
                        outputBT += " - L1";
                    if (BT.GetButton(Bluetooth.Button.L2))
                        outputBT += " - L2: " + BT.GetAnalogButton(Bluetooth.AnalogButton.L2);//Include analog readings - this can be done for almost all buttons                    
                    if (BT.GetButton(Bluetooth.Button.L3))
                        outputBT += " - L3";
                    if (BT.GetButton(Bluetooth.Button.R1))
                        outputBT += " - R1";
                    if (BT.GetButton(Bluetooth.Button.R2))
                        outputBT += " - R2: " + BT.GetAnalogButton(Bluetooth.AnalogButton.R2);//Include analog readings - this can be done for almost all buttons
                    if (BT.GetButton(Bluetooth.Button.R3))
                        outputBT += " - R3";

                    if (BT.GetButton(Bluetooth.Button.SELECT))
                    {
                        outputBT += " - Select";
                        outputBT += " - " + BT.GetStatusString();//Print status string
                    }
                    if (BT.GetButton(Bluetooth.Button.PS))
                    {                                
                        print4DOFBT = false;
                        PS3RumbleBT = false;      
                        BT.disconnectController();//Disconnect the controller                        
                        outputBT += " - PS";
                    }

                    if (BT.GetButton(Bluetooth.Button.START))
                    {
                        outputBT += " - Start";
                        PS3RumbleBT = !PS3RumbleBT;//Set rumble
                        if (PS3RumbleBT)
                            outputBT += " - Rumble is on";
                        else
                            outputBT += " - Rumble is off";
                        while (BT.GetButton(Bluetooth.Button.START)) ;//Wait for button to be released
                    }

                    if (print4DOFBT)
                        //Print angles                   
                        outputBT += " - Pitch: " + BT.GetAngle(Bluetooth.Angle.Pitch) + " - Roll: " + BT.GetAngle(Bluetooth.Angle.Roll) + " - Gyro: " + BT.GetSensor(Bluetooth.Sensor.gZ);

                    //Ignore all joystick values if too small - 127 is center
                    if (BT.GetAnalogHat(Bluetooth.AnalogHat.LeftHatX) > 137 || BT.GetAnalogHat(Bluetooth.AnalogHat.LeftHatX) < 117)
                        outputBT += " - LeftHatX: " + BT.GetAnalogHat(Bluetooth.AnalogHat.LeftHatX);
                    if (BT.GetAnalogHat(Bluetooth.AnalogHat.LeftHatY) > 137 || BT.GetAnalogHat(Bluetooth.AnalogHat.LeftHatY) < 117)
                        outputBT += " - LeftHatY: " + BT.GetAnalogHat(Bluetooth.AnalogHat.LeftHatY);
                    if (BT.GetAnalogHat(Bluetooth.AnalogHat.RightHatX) > 137 || BT.GetAnalogHat(Bluetooth.AnalogHat.RightHatX) < 117)
                        outputBT += " - RightHatX: " + BT.GetAnalogHat(Bluetooth.AnalogHat.RightHatX);
                    if (BT.GetAnalogHat(Bluetooth.AnalogHat.RightHatY) > 137 || BT.GetAnalogHat(Bluetooth.AnalogHat.RightHatY) < 117)
                        outputBT += " - RightHatY: " + BT.GetAnalogHat(Bluetooth.AnalogHat.RightHatY);

                    if (outputBT != "" && outputBT != lastoutputBT)//Check if output is not empty and not equal to the last one
                        WriteSerial("PS3 Controller" + outputBT);
                    lastoutputBT = outputBT;
                }
                for (byte i = 0; i < PS3Connected; i++)
                {
                    if (runThread[i])
                    {
                        output[i] = "";//Reset output

                        if (PS3[i].GetButton(PS3Controller.Button.TRIANGLE))
                            output[i] += " - Triangle";
                        if (PS3[i].GetButton(PS3Controller.Button.CIRCLE))
                            output[i] += " - Circle";
                        if (PS3[i].GetButton(PS3Controller.Button.CROSS))
                            output[i] += " - Cross";                        
                        if (PS3[i].GetButton(PS3Controller.Button.SQUARE))
                            output[i] += " - Square";

                        if (PS3[i].GetButton(PS3Controller.Button.UP))
                        {
                            output[i] += " - Up";
                            print4DOF[i] = true;//Start printing angles and gyro value
                        }
                        if (PS3[i].GetButton(PS3Controller.Button.RIGHT))
                        {
                            output[i] += " - Right";
                            PS3[i].SetBD_Addr(Bluetooth.BDaddr);//Set the bluetooth address into the controller
                            output[i] += " - Set BT Address: " + ByteToHex(Bluetooth.BDaddr[0]) + " " + ByteToHex(Bluetooth.BDaddr[1]) + " " + ByteToHex(Bluetooth.BDaddr[2]) + " " + ByteToHex(Bluetooth.BDaddr[3]) + " " + ByteToHex(Bluetooth.BDaddr[4]) + " " + ByteToHex(Bluetooth.BDaddr[5]);                            
                        }
                        if (PS3[i].GetButton(PS3Controller.Button.DOWN))
                        {
                            output[i] += " - Down";
                            print4DOF[i] = false;//Stop printing angles and gyro value
                        }
                        if (PS3[i].GetButton(PS3Controller.Button.LEFT))
                        {
                            output[i] += " - Left";
                            byte[] buffer = PS3[i].GetBD_Addr();//Read the bluetooth address in the controller
                            output[i] += " - Got BT Address: " + ByteToHex(buffer[0]) + " " + ByteToHex(buffer[1]) + " " + ByteToHex(buffer[2]) + " " + ByteToHex(buffer[3]) + " " + ByteToHex(buffer[4]) + " " + ByteToHex(buffer[5]);                            
                        }

                        if (PS3[i].GetButton(PS3Controller.Button.L1))
                            output[i] += " - L1";
                        if (PS3[i].GetButton(PS3Controller.Button.L2))
                            output[i] += " - L2: " + PS3[i].GetAnalogButton(PS3Controller.AnalogButton.L2);//Include analog readings - this can be done for almost all buttons
                        if (PS3[i].GetButton(PS3Controller.Button.L3))
                            output[i] += " - L3";
                        if (PS3[i].GetButton(PS3Controller.Button.R1))
                            output[i] += " - R1";
                        if (PS3[i].GetButton(PS3Controller.Button.R2))
                            output[i] += " - R2: " + PS3[i].GetAnalogButton(PS3Controller.AnalogButton.R2);//Include analog readings - this can be done for almost all buttons
                        if (PS3[i].GetButton(PS3Controller.Button.R3))
                            output[i] += " - R3";

                        if (PS3[i].GetButton(PS3Controller.Button.SELECT))
                        {
                            output[i] += " - Select";
                            output[i] += " - " + PS3[i].GetStatusString();//Print status string
                        }
                        if (PS3[i].GetButton(PS3Controller.Button.PS))
                            output[i] += " - PS";

                        //Ignore all joystick values if too small - 127 is center
                        if (PS3[i].GetAnalogHat(PS3Controller.AnalogHat.LeftHatX) > 137 || PS3[i].GetAnalogHat(PS3Controller.AnalogHat.LeftHatX) < 117)
                            output[i] += " - LeftHatX: " + PS3[i].GetAnalogHat(PS3Controller.AnalogHat.LeftHatX);
                        if (PS3[i].GetAnalogHat(PS3Controller.AnalogHat.LeftHatY) > 137 || PS3[i].GetAnalogHat(PS3Controller.AnalogHat.LeftHatY) < 117)
                            output[i] += " - LeftHatY: " + PS3[i].GetAnalogHat(PS3Controller.AnalogHat.LeftHatY);
                        if (PS3[i].GetAnalogHat(PS3Controller.AnalogHat.RightHatX) > 137 || PS3[i].GetAnalogHat(PS3Controller.AnalogHat.RightHatX) < 117)
                            output[i] += " - RightHatX: " + PS3[i].GetAnalogHat(PS3Controller.AnalogHat.RightHatX);
                        if (PS3[i].GetAnalogHat(PS3Controller.AnalogHat.RightHatY) > 137 || PS3[i].GetAnalogHat(PS3Controller.AnalogHat.RightHatY) < 117)
                            output[i] += " - RightHatY: " + PS3[i].GetAnalogHat(PS3Controller.AnalogHat.RightHatY);

                        if (print4DOF[i])                        
                            //Print angles                   
                            output[i] += " - Pitch: " + PS3[i].GetAngle(PS3Controller.Angle.Pitch) + " - Roll: " + PS3[i].GetAngle(PS3Controller.Angle.Roll) + " - Gyro: " + PS3[i].GetSensor(PS3Controller.Sensor.gZ);                        

                        if (PS3[i].GetButton(PS3Controller.Button.START))
                        {
                            output[i] += " - Start";
                            PS3Rumble[i] = !PS3Rumble[i];//Set rumble
                            if (PS3Rumble[i])
                                output[i] += " - Rumble is on";
                            else
                                output[i] += " - Rumble is off";
                            while (PS3[i].GetButton(PS3Controller.Button.START)) ;//Wait for button to be released
                        }
                        
                        if (output[i] != "" && output[i] != lastoutput[i])//Check if output is not empty and not equal to the last one
                            WriteSerial("PS3 Controller: " + (i + 1) + output[i]);
                        lastoutput[i] = output[i];
                    }
                }
            }
        }

        public static void Rumble()
        {
            bool[] rumbleOff = new bool[PS3Max];
            while (true)
            {
                for (byte i = 0; i < PS3Connected; i++)
                {
                    if (PS3Rumble[i])
                    {
                        WriteSerial("PS3 Controller: " + (i + 1) + " - RumbleHigh");
                        PS3[i].SetRumbleOn(PS3Controller.Rumble.RumbleHigh);//Rumble with high intensity
                        rumbleOff[i] = true;
                    }
                    Thread.Sleep(2000 / PS3Connected);//2 seconds delay
                }                                
                for (byte i = 0; i < PS3Connected; i++)
                {
                    if (PS3Rumble[i])
                    {
                        WriteSerial("PS3 Controller: " + (i + 1) + " - RumbleLow");
                        PS3[i].SetRumbleOn(PS3Controller.Rumble.RumbleLow);//Rumble with low intensity                        
                        rumbleOff[i] = true;
                    }
                    Thread.Sleep(2000 / PS3Connected);//2 seconds delay
                }
                
                for (byte i = 0; i < PS3Connected; i++)
                {
                    if (!PS3Rumble[i] && rumbleOff[i])
                    {
                        WriteSerial("PS3 Controller: " + (i + 1) + " - RumbleOff");
                        PS3[i].SetRumbleOff();
                        rumbleOff[i] = false;
                    }
                }
            }
        }
        public static void RumbleBT()
        {
            bool rumbleOff = false;//This should really be true, but then it will start by printing: "PS3 Controller: RumbleOff"
            while (true)
            {
                if (PS3RumbleBT)
                {
                    WriteSerial("PS3 Controller - RumbleHigh");
                    BT.hid_setRumbleOn(Bluetooth.Rumble.RumbleHigh);//Rumble with high intensity
                    rumbleOff = true;                    
                }
                Thread.Sleep(2000);//2 seconds delay
                
                if (PS3RumbleBT)
                {
                    WriteSerial("PS3 Controller - RumbleLow");
                    BT.hid_setRumbleOn(Bluetooth.Rumble.RumbleLow);//Rumble with low intensity                        
                    rumbleOff = true;
                }
                Thread.Sleep(2000);//2 seconds delay
                
                if (!PS3RumbleBT && rumbleOff)
                {
                    if (Bluetooth.PS3BTConnected || Bluetooth.PS3MoveBTConnected)
                    {
                        WriteSerial("PS3 Controller - RumbleOff");
                        BT.hid_setRumbleOff();
                        rumbleOff = false;
                    }
                }                 
            }
        }

        static void PS3SetLED(byte number)
        {
            PS3[number].SetLedOff(PS3Controller.LED.LED1);
            PS3[number].SetLedOff(PS3Controller.LED.LED2);
            PS3[number].SetLedOff(PS3Controller.LED.LED3);
            PS3[number].SetLedOff(PS3Controller.LED.LED4);

            if (number < 4)//Controller 1-4
                PS3[number].SetLedOn((PS3Controller.LED)(1 << number));

            else if (number < 7)//Controller 5-7
            {
                PS3[number].SetLedOn(PS3Controller.LED.LED4);
                PS3[number].SetLedOn((PS3Controller.LED)(1 << (number - 4)));
            }
            else if (number < 9)//Controller 8-9
            {
                PS3[number].SetLedOn(PS3Controller.LED.LED3);
                PS3[number].SetLedOn(PS3Controller.LED.LED4);
                PS3[number].SetLedOn((PS3Controller.LED)(1 << (number - 7)));
            }
            else//If you are lucky enough to have 10 controllers, it is supported
            {
                PS3[number].SetLedOn(PS3Controller.LED.LED1);
                PS3[number].SetLedOn(PS3Controller.LED.LED2);
                PS3[number].SetLedOn(PS3Controller.LED.LED3);
                PS3[number].SetLedOn(PS3Controller.LED.LED4);
            }
        }
        static void WriteSerial(string StringToWrite)
        {
            // convert the string to bytes
            byte[] buffer = Encoding.UTF8.GetBytes(StringToWrite + "\r\n");
            // send the bytes on the serial port
            UART.Write(buffer, 0, buffer.Length);
            //Debug.Print(StringToWrite);//Also print for debugging
        }
        public static string ByteToHex(byte b)//Used to convert the bluetooth address to hex
        {
            const string hex = "0123456789ABCDEF";
            int low = b & 0x0f;
            int high = b >> 4;
            string s = "0x" + new string(new char[] { hex[high], hex[low] });
            return s;
        }
    }
}