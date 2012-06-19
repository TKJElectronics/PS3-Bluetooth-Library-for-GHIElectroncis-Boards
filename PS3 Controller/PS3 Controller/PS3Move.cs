/*  
    A special thanks go to people behind these sites:
    http://thp.io/2010/psmove/
    http://www.copenhagengamecollective.org/unimove/    
    https://github.com/thp/psmoveapi
    http://code.google.com/p/moveonpc/         
*/

using System;
using System.Threading;
using Microsoft.SPOT;
using GHIElectronics.NETMF.USBHost;

using PS3ControllerBluetooth;

namespace PS3ControllerUSBMove
{
    class PS3Move
    {
        public static ushort PS3MOVE_VENDOR_ID = 0x054C;//Sony Corporation
        public static ushort PS3MOVE_PRODUCT_ID = 0x03D5;//Motion controller

        private USBH_RawDevice raw;
        private USBH_RawDevice.Pipe writePipe;               
        
        public  byte[] writeBuffer = new byte[7] 
        {
            0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00//0x02, 0x00, r, g, b, 0x00, rumble
        };
        Thread PS3MoveWriteThread;//The LED and rumble values, has to be written again and again, for it to stay turned on 
                    
        public PS3Move(USBH_Device device)
        {
            if (device.VENDOR_ID != PS3MOVE_VENDOR_ID || device.PRODUCT_ID != PS3MOVE_PRODUCT_ID)
                throw new InvalidOperationException();

            raw = new USBH_RawDevice(device);
            USBH_Descriptors.Configuration cd = raw.GetConfigurationDescriptors(0);

            writePipe = raw.OpenPipe(cd.interfaces[0].endpoints[0]); // to write settings (LEDs, rumble...)            
            writePipe.TransferTimeout = 0;
                                  
            //Set configuration
            raw.SendSetupTransfer(0x00, 0x09, cd.bConfigurationValue, 0x00);

            //Set the BD address automatically      
            SetBD_Addr(Bluetooth.BDaddr);

            SetLed(Bluetooth.Colors.Green);//Indicate that it's connected            

            PS3MoveWriteThread = new Thread(WriteThread);// create the write thread - needed for the LED's and rumble to stay turned on          
            PS3MoveWriteThread.Start();            
        }
        public void Abort()
        {
            PS3MoveWriteThread.Abort();             
        }
        public bool SetLed(byte r, byte g, byte b)
        {
            //set the LED's values into the write buffer
            writeBuffer[2] = r;
            writeBuffer[3] = g;
            writeBuffer[4] = b;

            try
            {
                //Transfer the bytes to the controller
                writePipe.TransferData(writeBuffer, 0, writeBuffer.Length);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
        public bool SetLed(Bluetooth.Colors color)
        {
            //set the LED's values into the write buffer
            writeBuffer[2] = (byte)((int)color >> 16);
            writeBuffer[3] = (byte)((int)color >> 8);
            writeBuffer[4] = (byte)(color);

            try
            {
                //Transfer the bytes to the controller
                writePipe.TransferData(writeBuffer, 0, writeBuffer.Length);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
        public bool SetRumble(byte rumble)
        {
            //set the rumble values into the write buffer
            writeBuffer[6] = rumble;                        

            try
            {
                //Transfer the bytes to the controller
                writePipe.TransferData(writeBuffer, 0, writeBuffer.Length);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public bool SetBD_Addr(byte[] btaddr)
        {
            byte[] buf = new byte[11];
            buf[0] = 0x05;
            buf[7] = 0x10;
            buf[8] = 0x01;
            buf[9] = 0x02;
            buf[10] = 0x12;

            for (int i = 0; i < 6; i++)
                buf[i + 1] = btaddr[5 - i];//Copy into buffer, has to be written reversed
            
            try
            {
                //Host to device (0x00) | Class (0x20) | Interface (0x01), Set Report (0x09), Report Type (Feature 0x03) - Report ID (0x05), Host to device (0x00) - Endpoint 0 (0x00), data, dataoffset, datalength
                raw.SendSetupTransfer(0x21, 0x09, 0x0305, 0x0000, buf, 0x00, buf.Length);
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
            byte[] buf = new byte[16];
            byte[] BDaddr = new byte[6];

            try
            {
                //Device to host (0x80) | Class (0x20) | Interface (0x01), Get Report (0x01), Report Type (Feature 0x03) - Report ID (0x04), Host to device (0x00) - Endpoint 0 (0x00), data, dataoffset, datalength
                raw.SendSetupTransfer(0xA1, 0x01, 0x0304, 0x0000, buf, 0x00, buf.Length);
            }
            catch (Exception)
            {
                Debug.Print("Error getting BD Address");
            }

            for (int i = 0; i < 6; i++)
            {
                BDaddr[i] = buf[15 - i];///Copy the Bluetooth address (6 bytes) to into BDaddr, reversed
                //Debug.Print(BDaddr[i].ToString());                                                
            }                        
            Debug.Print("Bluetooth Address: " + ByteToHex(BDaddr[0]) + " " + ByteToHex(BDaddr[1]) + " " + ByteToHex(BDaddr[2]) + " " + ByteToHex(BDaddr[3]) + " " + ByteToHex(BDaddr[4]) + " " + ByteToHex(BDaddr[5]));

            return BDaddr;
        }
        public byte[] GetCalibration()
        {
            byte[] buf = new byte[49];
            byte[] Calibration = new byte[147];

            for (byte i = 0; i < 3; i++)
            {
                try
                {
                    //Device to host (0x80) | Class (0x20) | Interface (0x01), Get Report (0x01), Report Type (Feature 0x03) - Report ID (0x04), Host to device (0x00) - Endpoint 0 (0x00), data, dataoffset, datalength
                    raw.SendSetupTransfer(0xA1, 0x01, 0x0310, 0x0000, buf, 0x00, buf.Length);
                }
                catch (Exception)
                {
                    Debug.Print("Error getting BD Address");
                }
                for (byte i2 = 0; i2 < buf.Length; i2++)
                    Calibration[49 * i + i2] = buf[i2];
            }

            return Calibration;
        }
        private void WriteThread()
        {
            while (true)
            {
                //Write once every 4.5 secound
                Thread.Sleep(4500);
                try
                {
                    writePipe.TransferData(writeBuffer, 0, writeBuffer.Length);
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