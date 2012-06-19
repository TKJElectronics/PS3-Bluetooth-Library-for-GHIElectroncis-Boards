using System;
using System.Threading;
using Microsoft.SPOT;
using GHIElectronics.NETMF.System;
using GHIElectronics.NETMF.USBHost;

using PS3ControllerBluetooth;

namespace PS3ControllerUSB
{
    class PS3Controller
    {
        public static ushort PS3_VENDOR_ID = 0x054C;//Sony Corporation
        public static ushort PS3_PRODUCT_ID = 0x0268;//PS3 Controller DualShock 3

        private USBH_RawDevice raw;
        private USBH_RawDevice.Pipe readPipe;
        private byte[] readBuffer = new byte[64];
        private byte[] writeBuffer = new byte[64];
        //command to send data
        public static byte[] enableUSB = new byte[] { 0x42, 0x0c, 0x00, 0x00 };
        //output report buffer, used for setting the LED and rumble on and off
        public static byte[] OUTPUT_REPORT_BUFFER = new byte[48] 
        {
            0x00, 0x00, 0x00, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x00, 0x00, 
            0xff, 0x27, 0x10, 0x00, 0x32, 
            0xff, 0x27, 0x10, 0x00, 0x32, 
            0xff, 0x27, 0x10, 0x00, 0x32, 
            0xff, 0x27, 0x10, 0x00, 0x32, 
            0x00, 0x00, 0x00, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 
        };

        Thread PS3Thread;
        public PS3Controller(USBH_Device device)
        {
            if (device.VENDOR_ID != PS3_VENDOR_ID || device.PRODUCT_ID != PS3_PRODUCT_ID)
                throw new InvalidOperationException();

            raw = new USBH_RawDevice(device);
            USBH_Descriptors.Configuration cd = raw.GetConfigurationDescriptors(0);
            
            readPipe = raw.OpenPipe(cd.interfaces[0].endpoints[1]); // to read buttons
            readPipe.TransferTimeout = 0;
            //Set configuration
            raw.SendSetupTransfer(0x00, 0x09, cd.bConfigurationValue, 0x00);

            //Set the BD address automatically
            SetBD_Addr(Bluetooth.BDaddr);

            try
            {
                //request the PS3 controller to send button presses etc back
                //Host to device (0x00) | Class (0x20) | Interface (0x01), Set Report (0x09), Report Type (Feature 0x03) - Report ID (0xF4), Host to device (0x00) - Endpoint 0 (0x00), data, dataoffset, datalength
                raw.SendSetupTransfer(0x21, 0x09, 0x03F4, 0x0000, enableUSB, 0x00, 0x04);             
            }
            catch (Exception ex)
            {                
                Debug.Print("==============================");
                Debug.Print(DateTime.Now.ToString());
                Debug.Print(ex.Message);
                Debug.Print(ex.StackTrace);
                if (ex.InnerException != null)
                {
                    Debug.Print("Inner Exception: " + ex.InnerException.Message);
                }
            }

            for (int i = 0; i < OUTPUT_REPORT_BUFFER.Length; i++)
                writeBuffer[i] = OUTPUT_REPORT_BUFFER[i];

            PS3Thread = new Thread(ReaderThread);         // create the polling thread
            PS3Thread.Priority = ThreadPriority.Highest;  // we should read as fast as possible
            PS3Thread.Start();
        }

        public void Abort()//Abort the thread
        {
            PS3Thread.Abort();
        }

        public enum Button
        {
            // byte location | bit location
            SELECT = (2 << 8) | 0x01,
            L3 = (2 << 8) | 0x02,
            R3 = (2 << 8) | 0x04,
            START = (2 << 8) | 0x08,
            UP = (2 << 8) | 0x10,
            RIGHT = (2 << 8) | 0x20,
            DOWN = (2 << 8) | 0x40,
            LEFT = (2 << 8) | 0x80,

            L2 = (3 << 8) | 0x01,
            R2 = (3 << 8) | 0x02,
            L1 = (3 << 8) | 0x04,
            R1 = (3 << 8) | 0x08,
            TRIANGLE = (3 << 8) | 0x10,
            CIRCLE = (3 << 8) | 0x20,
            CROSS = (3 << 8) | 0x40,
            SQUARE = (3 << 8) | 0x80,

            PS = (4 << 8) | 0x01,
        }
        public enum AnalogButton
        {
            UP = 14,
            RIGHT = 15,
            DOWN = 16,
            LEFT = 17,

            L2 = 18,
            R2 = 19,
            L1 = 20,
            R1 = 21,
            TRIANGLE = 22,
            CIRCLE = 23,
            CROSS = 24,
            SQUARE = 25,
        }
        public enum AnalogHat
        {
            LeftHatX = 6,
            LeftHatY = 7,
            RightHatX = 8,
            RightHatY = 9,
        }
        public enum Sensor
        {
            aX = 41,
            aY = 43,
            aZ = 45,
            gZ = 47,
        }
        public enum Angle
        {
            Pitch = 0x01,
            Roll = 0x02,
        }
        public enum Status
        {
            // byte location | bit location
            Plugged = (29 << 8) | 0x02,
            Unplugged = (29 << 8) | 0x03,

            Charging = (30 << 8) | 0xEE,
            NotCharging = (30 << 8) | 0xF1,
            Shutdown = (30 << 8) | 0x01,
            Dying = (30 << 8) | 0x02,
            Low = (30 << 8) | 0x03,
            High = (30 << 8) | 0x04,
            Full = (30 << 8) | 0x05,

            CableRumble = (31 << 8) | 0x10,//Opperating by USB and rumble is turned on
            Cable = (31 << 8) | 0x12,//Opperating by USB and rumble is turned off   
            BluetoothRumble = (31 << 8) | 0x14,//Opperating by bluetooth and rumble is turned on
            Bluetooth = (31 << 8) | 0x16,//Opperating by bluetooth and rumble is turned off     
        }

        public enum LED
        {
            LED1 = 0x01,
            LED2 = 0x02,
            LED3 = 0x04,
            LED4 = 0x08,

            LED5 = 0x09,
            LED6 = 0x0A,
            LED7 = 0x0C,
            LED8 = 0x0D,
            LED9 = 0x0E,
            LED10 = 0x0F,
        }
        public enum Rumble
        {
            RumbleHigh = 0x10,
            RumbleLow = 0x20,
        }

        public bool GetButton(Button b)
        {
            if (readBuffer == null)
                return false;

            if ((readBuffer[(uint)b >> 8] & ((byte)b & 0xff)) > 0)
                return true;
            return false;
        }
        public byte GetAnalogButton(AnalogButton a)
        {
            if (readBuffer == null)
                return 0;
            return (byte)(readBuffer[(uint)a]);
        }
        public byte GetAnalogHat(AnalogHat a)
        {
            if (readBuffer == null)
                return 0;
            return (byte)(readBuffer[(uint)a]);
        }
        public short GetSensor(Sensor a)
        {
            if (readBuffer == null)
                return 0;
            return (short)((readBuffer[(uint)a] << 8) | readBuffer[(uint)a + 1]);
        }
        public short GetAngle(Angle a)
        {
            double accXin;
            double accXval;
            double Pitch;

            double accYin;
            double accYval;
            double Roll;

            double accZin;
            double accZval;

            //Data for the Kionix KXPC4 used in DualShock 3
            double sensivity = 204.6;//0.66/3.3*1023 (660mV/g)
            double zeroG = 511.5;//1.65/3.3*1023 (1,65V)
            double R;//force vector

            accXin = GetSensor(PS3Controller.Sensor.aX);
            accXval = (zeroG - accXin) / sensivity;//Convert to g's
            accXval *= 2;

            accYin = GetSensor(PS3Controller.Sensor.aY);
            accYval = (zeroG - accYin) / sensivity;//Convert to g's
            accYval *= 2;

            accZin = GetSensor(PS3Controller.Sensor.aZ);
            accZval = (zeroG - accZin) / sensivity;//Convert to g's
            accZval *= 2;

            //Debug.Print("accXin: " + accXin + " accYin: " + accYin + " accZin: " + accZin);
            //Debug.Print("aX: " + accXval + " aY: " + accYval + " aZ: " + accZval);

            R = MathEx.Sqrt(MathEx.Pow(accXval, 2) + MathEx.Pow(accYval, 2) + MathEx.Pow(accZval, 2));
            
            if (a == PS3Controller.Angle.Pitch)
            {
                //the result will come out as radians, so it is multiplied by 180/pi, to convert to degrees
                //In the end it is minus by 90, so its 0 degrees when in horizontal postion
                Pitch = MathEx.Acos(accXval / R) * 180 / MathEx.PI - 90;
                //Debug.Print("accXangle: " + accXangle);

                if (accZval < 0)//Convert to 360 degrees resolution - uncomment if you need both pitch and roll
                {
                    if (Pitch < 0)
                        Pitch = -180 - Pitch;
                    else
                        Pitch = 180 - Pitch;
                }
                return (short)Pitch;

            }
            else
            {
                //the result will come out as radians, so it is multiplied by 180/pi, to convert to degrees
                //In the end it is minus by 90, so its 0 degrees when in horizontal postion
                Roll = MathEx.Acos(accYval / R) * 180 / MathEx.PI - 90;
                //Debug.Print("accYangle: " + accYangle);

                if (accZval < 0)//Convert to 360 degrees resolution - uncomment if you need both pitch and roll
                {
                    if (Roll < 0)
                        Roll = -180 - Roll;
                    else
                        Roll = 180 - Roll;
                }
                return (short)Roll;
            }                              
        }
        public bool GetStatus(Status c)
        {
            if (readBuffer == null)
                return false;
            if (readBuffer[(uint)c >> 8] == ((byte)c & 0xff))
                return true;
            return false;
        }
        public string GetStatusString()
        {
            string ConnectionStatus = "";
            if (GetStatus(Status.Plugged)) ConnectionStatus = "Plugged";           
            else if (GetStatus(Status.Unplugged)) ConnectionStatus = "Unplugged";            
            else ConnectionStatus = "Error";

            string PowerRating = "";
            if (GetStatus(Status.Charging)) PowerRating = "Charging";
            else if (GetStatus(Status.NotCharging)) PowerRating = "Not Charging";
            else if (GetStatus(Status.Shutdown)) PowerRating = "Shutdown";            
            else if (GetStatus(Status.Dying)) PowerRating = "Dying";
            else if (GetStatus(Status.Low)) PowerRating = "Low";
            else if (GetStatus(Status.High)) PowerRating = "High";
            else if (GetStatus(Status.Full)) PowerRating = "Full";
            else PowerRating = "Error: " + readBuffer[30];

            string WirelessStatus = "";
            if (GetStatus(Status.CableRumble)) WirelessStatus = "Cable - Rumble is on";
            else if (GetStatus(Status.Cable)) WirelessStatus = "Cable - Rumble is off";
            else if (GetStatus(Status.BluetoothRumble)) WirelessStatus = "Bluetooth - Rumble is on";
            else if (GetStatus(Status.Bluetooth)) WirelessStatus = "Bluetooth - Rumble is off";
            else WirelessStatus = "Error";

            return ("ConnectionStatus: " + ConnectionStatus + " - PowerRating: " + PowerRating + " - WirelessStatus: " + WirelessStatus);
        }

        public bool SetAllOff()
        {        
            //Write the standard command to turn rumble and LED's off
            for (int i = 0; i < OUTPUT_REPORT_BUFFER.Length; i++)
                writeBuffer[i] = OUTPUT_REPORT_BUFFER[i];

            try
            {
                //Host to device (0x00) | Class (0x20) | Interface (0x01), Set Report (0x09), Report Type (Output 0x02) - Report ID (0x01), Host to device (0x00) - Endpoint 0 (0x00), data, dataoffset, datalength
                raw.SendSetupTransfer(0x21, 0x09, 0x0201, 0x0000, writeBuffer, 0x00, 0x30);
            }
            catch (Exception)
            {
                return false;
            }
            return true;

        }
        public bool SetLedOff(LED a)
        {
            //check if LED is already off
            if ((byte)((byte)(((uint)a << 1) & writeBuffer[9])) != 0)
            {
                //set the LED into the write buffer
                writeBuffer[9] = (byte)((byte)(((uint)a & 0x0f) << 1) ^ writeBuffer[9]);
                try
                {
                    //Host to device (0x00) | Class (0x20) | Interface (0x01), Set Report (0x09), Report Type (Output 0x02) - Report ID (0x01), Host to device (0x00) - Endpoint 0 (0x00), data, dataoffset, datalength
                    raw.SendSetupTransfer(0x21, 0x09, 0x0201, 0x0000, writeBuffer, 0x00, 0x30);
                }
                catch (Exception)
                {
                    return false;
                }
                return true;
            }
            return false;
        }
        public bool SetLedOn(LED a)
        {
            //set the LED into the write buffer
            writeBuffer[9] = (byte)((byte)(((uint)a & 0x0f) << 1) | writeBuffer[9]);

            try
            {
                //Host to device (0x00) | Class (0x20) | Interface (0x01), Set Report (0x09), Report Type (Output 0x02) - Report ID (0x01), Host to device (0x00) - Endpoint 0 (0x00), data, dataoffset, datalength
                raw.SendSetupTransfer(0x21, 0x09, 0x0201, 0x0000, writeBuffer, 0x00, 0x30);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
        
        public bool SetRumbleOff()
        {
            writeBuffer[1] = 0x00;
            writeBuffer[2] = 0x00;//low mode off
            writeBuffer[3] = 0x00;
            writeBuffer[4] = 0x00;//high mode off

            try
            {
                //Host to device (0x00) | Class (0x20) | Interface (0x01), Set Report (0x09), Report Type (Output 0x02) - Report ID (0x01), Host to device (0x00) - Endpoint 0 (0x00), data, dataoffset, datalength
                raw.SendSetupTransfer(0x21, 0x09, 0x0201, 0x0000, writeBuffer, 0x00, 0x30);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public bool SetRumbleOn(Rumble a)
        {
            /*Still not totally sure how it works, maybe something like this instead?
             * 3 - duration_right
             * 4 - power_right
             * 5 - duration_left
             * 6 - power_left
             */
            if (((uint)a & 0x30) > 0)
            {
                writeBuffer[1] = 0xfe;
                writeBuffer[3] = 0xfe;
                if (((uint)a & 0x10) > 0)
                {
                    writeBuffer[2] = 0;//low mode off
                    writeBuffer[4] = 0xff;//high mode on
                }
                else
                {
                    writeBuffer[4] = 0;//high mode off
                    writeBuffer[2] = 0xff;//low mode on
                }
            }

            try
            {
                //Host to device (0x00) | Class (0x20) | Interface (0x01), Set Report (0x09), Report Type (Output 0x02) - Report ID (0x01), Host to device (0x00) - Endpoint 0 (0x00), data, dataoffset, datalength
                raw.SendSetupTransfer(0x21, 0x09, 0x0201, 0x0000, writeBuffer, 0x00, 0x30);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
        public bool SetBD_Addr(byte[] btaddr)
        {
            byte[] buf = new byte[8];
            buf[0] = 0x01;
            buf[1] = 0x00;
            for (int i = 0; i < 6; i++)
            {
                buf[i + 2] = btaddr[i];
            }
            try
            {
                //Host to device (0x00) | Class (0x20) | Interface (0x01), Set Report (0x09), Report Type (Feature 0x03) - Report ID (0xF5), Host to device (0x00) - Endpoint 0 (0x00), data, dataoffset, datalength
                raw.SendSetupTransfer(0x21, 0x09, 0x03F5, 0x0000, buf, 0x00, 0x08);
            }
            catch (Exception)
            {
                Debug.Print("Error setting BD Address");
                return false;
            }
            return true;
        }
        public byte[] GetBD_Addr()
        {
            byte[] buf = new byte[8];
            byte[] BDaddr = new byte[6];

            try
            {
                //Device to host (0x80) | Class (0x20) | Interface (0x01), Get Report (0x01), Report Type (Feature 0x03) - Report ID (0xF5), Host to device (0x00) - Endpoint 0 (0x00), data, dataoffset, datalength
                raw.SendSetupTransfer(0xA1, 0x01, 0x03F5, 0x0000, buf, 0x00, 0x08);
            }
            catch (Exception)
            {
                Debug.Print("Error getting BD Address");
            }
            
            for (int i = 0; i < 6; i++)
            {
                BDaddr[i] = buf[i+2];
                Debug.Print(BDaddr[i].ToString());                
            }
            
            return BDaddr;
        }

        private void ReaderThread()
        {            
            while (true)
            {
                //Read every bInterval
                Thread.Sleep(readPipe.PipeEndpoint.bInterval);
                try
                {
                    readPipe.TransferData(readBuffer, 0, readBuffer.Length);
                    // for debugging
                    /*
                    int i = 0;
                    Debug.Print(
                        ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " +
                        ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " +
                        ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " +
                        ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " +
                        ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " +
                        ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " +
                        ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " +
                        ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " +
                        ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " +
                        ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " +
                        ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " +
                        ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++]) + " " + ByteToHex(readBuffer[i++])
                        );
                     */
                }
                catch (Exception ex)
                {
                    Debug.Print("==============================");
                    Debug.Print(DateTime.Now.ToString());
                    Debug.Print(ex.Message);
                    Debug.Print(ex.StackTrace);
                    if (ex.InnerException != null)
                    {
                        Debug.Print("Inner Exception: " + ex.InnerException.Message);
                    }
                    Thread.Sleep(100);
                }
            }
        }

        /*
        //added this for debug printing
        public static string ByteToHex(byte b)
        {
            const string hex = "0123456789ABCDEF";
            int low = b & 0x0f;
            int high = b >> 4;
            string s = new string(new char[] { hex[high], hex[low] });
            return s;
        }
        */

    }
}

