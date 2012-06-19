/*
    The navigation controller works the same way as the original PS3 Controller - you have to write a special command before you can read the data.
    Actually the bytes of the buttons are at the same place, as the Dualshock 3 controller .
*/ 

using System;
using System.Threading;
using Microsoft.SPOT;
using GHIElectronics.NETMF.USBHost;

using PS3ControllerUSB;
using PS3ControllerBluetooth;

namespace PS3ControllerUSBNavigation
{
    class PS3Navigation
    {
        public static bool PS3NavigationConnected;

        public static ushort PS3NAVIGATION_VENDOR_ID = 0x054C;//Sony Corporation
        public static ushort PS3NAVIGATION_PRODUCT_ID = 0x042F;//Navigation controller

        private USBH_RawDevice raw;
        private USBH_RawDevice.Pipe readPipe;
        private byte[] readBuffer = new byte[64];
                              
        Thread PS3ReadThread;//The LED and rumble values, has to be written again and again, for it to stay turned on 

        public PS3Navigation(USBH_Device device)
        {
            if (device.VENDOR_ID != PS3NAVIGATION_VENDOR_ID || device.PRODUCT_ID != PS3NAVIGATION_PRODUCT_ID)
                throw new InvalidOperationException();

            raw = new USBH_RawDevice(device);
            USBH_Descriptors.Configuration cd = raw.GetConfigurationDescriptors(0);
            
            readPipe = raw.OpenPipe(cd.interfaces[0].endpoints[1]);                        
            readPipe.TransferTimeout = 0;            
                                  
            //Set configuration
            raw.SendSetupTransfer(0x00, 0x09, cd.bConfigurationValue, 0x00);

            //Set the BD address automatically
            SetBD_Addr(Bluetooth.BDaddr);

            try
            {
                //request the PS3 controller to send button presses etc back
                //Host to device (0x00) | Class (0x20) | Interface (0x01), Set Report (0x09), Report Type (Feature 0x03) - Report ID (0xF4), Host to device (0x00) - Endpoint 0 (0x00), data, dataoffset, datalength
                raw.SendSetupTransfer(0x21, 0x09, 0x03F4, 0x0000, PS3Controller.enableUSB, 0x00, 0x04);
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

            PS3NavigationConnected = true;
            PS3ReadThread = new Thread(ReaderThread);// create the write thread - needed for the LED's and rumble to stay turned on  
            PS3ReadThread.Priority = ThreadPriority.Highest;
            PS3ReadThread.Start();            
        }
        public void Abort()
        {
            PS3NavigationConnected = false;
            PS3ReadThread.Abort();            
        }
        public enum Button
        {
            // byte location | bit location
            L3 = (2 << 8) | 0x02,            
            
            UP = (2 << 8) | 0x10,
            RIGHT = (2 << 8) | 0x20,
            DOWN = (2 << 8) | 0x40,
            LEFT = (2 << 8) | 0x80,

            L2 = (3 << 8) | 0x01,            
            L1 = (3 << 8) | 0x04,
            
            
            CIRCLE = (3 << 8) | 0x20,
            CROSS = (3 << 8) | 0x40,            

            PS = (4 << 8) | 0x01,
        }
        public enum AnalogButton
        {
            UP = 14,
            RIGHT = 15,
            DOWN = 16,
            LEFT = 17,

            L2 = 18,            
            L1 = 20,
                        
            CIRCLE = 23,
            CROSS = 24,            
        }
        public enum AnalogHat
        {
            LeftHatX = 6,
            LeftHatY = 7,
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
                BDaddr[i] = buf[i + 2];
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
        public static string ByteToHex(byte b)//Used to convert the bluetooth address to hex
        {
            const string hex = "0123456789ABCDEF";
            int low = b & 0x0f;
            int high = b >> 4;
            string s = "0x" + new string(new char[] { hex[high], hex[low] });
            return s;
        }
        */ 
    }
}