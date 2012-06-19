using System;
using System.Text;
using System.IO.Ports;
using System.Threading;
using Microsoft.SPOT;
using GHIElectronics.NETMF.System;
using GHIElectronics.NETMF.USBHost;

using PS3ControllerUSB;

namespace PS3ControllerBluetooth
{
    class Bluetooth
    {
        //HCI event flags        
        public enum hci_event
        {
            HCI_FLAG_CMD_COMPLETE = 0x01,
            HCI_FLAG_CMD_STATUS = 0x02,
            HCI_FLAG_CONN_COMPLETE = 0x04,
            HCI_FLAG_DISCONN_COMPLETE = 0x08,
            HCI_FLAG_INCOMING_REQUEST = 0x10,
            HCI_FLAG_REMOTE_NAME = 0x20,    
        }
        hci_event hci_event_flag;

        //HCI Events        
        const byte EV_COMMAND_COMPLETE = 0x0e;
        const byte EV_COMMAND_STATUS = 0x0f;
        const byte EV_CONNECT_COMPLETE = 0x03;
        const byte EV_DISCONNECT_COMPLETE = 0x05;
        const byte EV_NUM_COMPLETE_PKT = 0x13;
        const byte EV_INCOMING_CONNECT = 0x04;
        const byte EV_ROLE_CHANGED = 0x12;
        const byte EV_REMOTE_NAME_COMPLETE = 0x07;

        //HCI state - used to set up bluetooth dongle
        const byte HCI_INIT_STATE = 0;
        const byte HCI_RESET_STATE = 1;                
        const byte HCI_BDADDR_STATE = 2;
        const byte HCI_SCANNING_STATE = 3;
        const byte HCI_CONNECT_IN_STATE = 4;
        const byte HCI_REMOTE_NAME = 5;             
        const byte HCI_CONNECTED_STATE = 6;
        const byte HCI_DISABLE_SCAN = 7;
        const byte HCI_DONE_STATE = 8;
        const byte HCI_DISCONNECT_STATE = 9;

        byte hci_state = HCI_INIT_STATE;
        
        uint hci_counter; // counter used for bluetooth hci reset loop        

        //variables filled from HCI event management
        byte hci_command_packets; //how many packets can host send to controller                
        int hci_handle;
        byte[] my_bdaddr = new byte[6];// bluetooth address stored least significant byte first        
        byte dev_role;
        byte[] disc_bdaddr = new byte[6];// Device Bluetooth Address         
        byte[] remote_name = new byte[40];// first 40 chars of name

        bool remote_name_bool;// Indicate if reading remote name
                
        /* Bluetooth L2CAP states for L2CAP_task() */
        const byte L2CAP_EV_WAIT = 0;
        const byte L2CAP_EV_CONTROL_SETUP = 1;
        const byte L2CAP_EV_CONTROL_REQUEST = 2;
        const byte L2CAP_EV_CONTROL_SUCCESS = 3;        

        const byte L2CAP_EV_INTERRUPT_SETUP = 4;
        const byte L2CAP_EV_INTERRUPT_REQUEST = 5;
        const byte L2CAP_EV_INTERRUPT_SUCCESS = 6;

        const byte L2CAP_EV_HID_ENABLE_SIXAXIS = 7;                
        const byte L2CAP_EV_L2CAP_DONE = 8;

        const byte L2CAP_EV_INTERRUPT_DISCONNECT = 9;
        const byte L2CAP_EV_CONTROL_DISCONNECT = 10;

        byte l2cap_state = L2CAP_EV_WAIT;

        // Used For Connection Response - Remember to Include High Byte
        const byte PENDING = 0x01;
        const byte SUCCESSFUL = 0x00;

        /* L2CAP event flags */
        public enum l2cap_event
        {                        
            L2CAP_EV_CONTROL_CONNECTION_REQUEST = 0x01,
            L2CAP_EV_CONTROL_CONFIG_REQUEST = 0x02,
            L2CAP_EV_CONTROL_CONFIG_SUCCESS = 0x04,

            L2CAP_EV_INTERRUPT_CONNECTION_REQUEST = 0x08,
            L2CAP_EV_INTERRUPT_CONFIG_REQUEST = 0x10,
            L2CAP_EV_INTERRUPT_CONFIG_SUCCESS = 0x20,

            L2CAP_EV_CONTROL_DISCONNECT_RESPONSE = 0x40,
            L2CAP_EV_INTERRUPT_DISCONNECT_RESPONSE = 0x80, 
        }
        l2cap_event l2cap_event_status;

        /* L2CAP signaling commands */
        const byte L2CAP_CMD_COMMAND_REJECT = 0x01;
        const byte L2CAP_CMD_CONNECTION_REQUEST = 0x02;
        const byte L2CAP_CMD_CONNECTION_RESPONSE = 0x03;
        const byte L2CAP_CMD_CONFIG_REQUEST = 0x04;
        const byte L2CAP_CMD_CONFIG_RESPONSE = 0x05;
        const byte L2CAP_CMD_DISCONNECT_REQUEST = 0x06;
        const byte L2CAP_CMD_DISCONNECT_RESPONSE = 0x07;

        /* L2CAP Channels */
        byte[] control_scid = new byte[2];// L2CAP source CID for HID_Control                
        byte[] control_dcid = new byte[2] { 0x40, 0x00 };//0x0040        
        byte[] interrupt_scid = new byte[2];// L2CAP source CID for HID_Interrupt        
        byte[] interrupt_dcid = new byte[2] { 0x41, 0x00 };//0x0041
        byte identifier;//Identifier for connection        

        /* Bluetooth L2CAP PSM */
        const byte L2CAP_PSM_HID_CTRL = 0x11;// HID_Control        
        const byte L2CAP_PSM_HID_INTR = 0x13;// HID_Interrupt        
                      
        //Bluetooth    
        public static byte[] BDaddr = new byte[6] { 0x00, 0x1F, 0x81, 0x00, 0x08, 0x30 };//My bluetooth dongle's address - change it to yours
        public static ushort CSR_VENDOR_ID = 0x0A12;//Cambridge Silicon Radio Ltd.
        public static ushort CSR_PRODUCT_ID = 0x001;//Bluetooth dongle
        public static bool PS3BTConnected;// Variable used to indicate if the normal playstation controller is successfully connected
        public static bool PS3MoveBTConnected;// Variable used to indicate if the move controller is successfully connected
        public static bool PS3NavigationBTConnected;// Variable used to indicate if the navigation controller is successfully connected

        private USBH_RawDevice raw;
        private USBH_RawDevice.Pipe IntInPipe, BulkInPipe, BulkOutPipe;

        private byte[] IntInBuffer = new byte[16];// Interrupt in buffer
        private byte[] HCIBuffer = new byte[16];// Used to store HCI commands
        
        private byte[] BulkInBuffer = new byte[64];// Bulk in buffer
        private byte[] L2CAPBuffer = new byte[64];// Used to store L2CAP commands
        private byte[] HIDBuffer = new byte[64];// Used to store HID commands
        private byte[] HIDMoveBuffer = new byte[50];// Used to store HID commands for the Move controller
        
        Thread IntInThread;// Interrupt in thread
        Thread BulkInThread;// Bulk in thread        

        long timerHID;// timer used see if there has to be a delay before a new HID command
        long dtimeHID;// delta time since last HID command

        long timerLEDRumble;// used to continuously set PS3 Move controller LED and rumble values
        long dtimeLEDRumble;// used to know how longs since last since the LED and rumble values was written

        //Setup serial connection
        static SerialPort UART = new SerialPort("COM2", 115200);

        public Bluetooth(USBH_Device device)
        {            
            if (device.VENDOR_ID != CSR_VENDOR_ID || device.PRODUCT_ID != CSR_PRODUCT_ID)//Check if the device is a Bluetooth dongle
                throw new InvalidOperationException();

            raw = new USBH_RawDevice(device);
            USBH_Descriptors.Configuration cd = raw.GetConfigurationDescriptors(0);

            IntInPipe = raw.OpenPipe(cd.interfaces[0].endpoints[0]); // Interrupt In
            BulkInPipe = raw.OpenPipe(cd.interfaces[0].endpoints[1]); // Bulk In
            BulkOutPipe = raw.OpenPipe(cd.interfaces[0].endpoints[2]); // Bulk Out

            //Add transfer timeout for better stability
            IntInPipe.TransferTimeout = 5;
            BulkInPipe.TransferTimeout = 5;

            //Set configuration
            raw.SendSetupTransfer(0x00, 0x09, cd.bConfigurationValue, 0x00);

            //Needed for PS3 Dualshock Controller to work
            for (int i = 0; i < PS3Controller.OUTPUT_REPORT_BUFFER.Length; i++)
                HIDBuffer[i + 2] = PS3Controller.OUTPUT_REPORT_BUFFER[i];//First two bytes reserved for report type and ID

            HIDBuffer[0] = 0x52;// HID BT Set_report (0x50) | Report Type (Output 0x02)
            HIDBuffer[1] = 0x01;// Report ID

            //Needed for PS3 Move Controller commands to work
            HIDMoveBuffer[0] = 0xA2;// HID BT DATA_request (0xA0) | Report Type (Output 0x02)            
            HIDMoveBuffer[1] = 0x02;// Report ID            

            IntInThread = new Thread(IntReadingThread);            
            IntInThread.Start();

            BulkInThread = new Thread(BulkReadingThread);
            BulkInThread.Priority = ThreadPriority.Highest;
            BulkInThread.Start();            

            WriteSerial("CSR Initialized");
        }
        public void Abort()
        {
            if (PS3BTConnected || PS3MoveBTConnected || PS3NavigationBTConnected)//Disconnect controller if it's in use
                disconnectController();
            IntInThread.Abort();
            BulkInThread.Abort();            
        }

        private void IntReadingThread()
        {
            while (true)
            {
                //Read every bInterval
                Thread.Sleep(IntInPipe.PipeEndpoint.bInterval);
                {
                    try
                    {
                        IntInPipe.TransferData(IntInBuffer, 0, IntInBuffer.Length);                        
                        // uncomment for debugging
                        /*
                            int i = 0;
                            WriteSerial(
                                ByteToHex(IntInBuffer[i++]) + " " + ByteToHex(IntInBuffer[i++]) + " " + ByteToHex(IntInBuffer[i++]) + " " + ByteToHex(IntInBuffer[i++]) + " " +
                                ByteToHex(IntInBuffer[i++]) + " " + ByteToHex(IntInBuffer[i++]) + " " + ByteToHex(IntInBuffer[i++]) + " " + ByteToHex(IntInBuffer[i++]) + " " +
                                ByteToHex(IntInBuffer[i++]) + " " + ByteToHex(IntInBuffer[i++]) + " " + ByteToHex(IntInBuffer[i++]) + " " + ByteToHex(IntInBuffer[i++]) + " " +
                                ByteToHex(IntInBuffer[i++]) + " " + ByteToHex(IntInBuffer[i++]) + " " + ByteToHex(IntInBuffer[i++]) + " " + ByteToHex(IntInBuffer[i++])
                                );
                        */
                        if (remote_name_bool)
                        {
                            for (byte i2 = 0; i2 < IntInBuffer.Length; i2++)
                            {
                                if (IntInBuffer[i2] == 0x00)
                                {                                    
                                    Debug.Print("Finished Reading Name");                                                                                                                                                                                                                   
                                    remote_name_bool = false;
                                    hci_event_flag |= hci_event.HCI_FLAG_REMOTE_NAME;
                                    break;
                                }
                                if (IntInBuffer[0] == 0x54 || IntInBuffer[0] == 0x43 || IntInBuffer[0] == 0x69)// Second Line - 'T' in "PLAYSTATION(R)3 Controller" or 'C' in "Motion Controller" or 'i' in "Navigation Controller"
                                    remote_name[7 + i2] = IntInBuffer[i2];
                                else if (IntInBuffer[0] == 0x6C)// Third Line - "l" in "PLAYSTATION(R)3 Controller"
                                    remote_name[23 + i2] = IntInBuffer[i2];
                                else
                                    break;
                            }
                        }
                        switch (IntInBuffer[0])
                        {
                            /*  buf[0] = Event Code                            */
                            /*  buf[1] = Parameter Total Length                */
                            /*  buf[n] = Event Parameters based on each event  */

                            case EV_REMOTE_NAME_COMPLETE:
                                for (byte n = 0; n < remote_name.Length; n++)//Reset remote name
                                    remote_name[n] = 0;

                                for (byte n = 9; n < IntInBuffer.Length; n++)
                                    remote_name[n - 9] = IntInBuffer[n];
                                remote_name_bool = true;
                                break;

                            case EV_COMMAND_COMPLETE:
                                //Debug.Print("Command Complete");
                                hci_command_packets = IntInBuffer[2];//Update flow control    
                                if (IntInBuffer[5] != 0x00)// Check if success
                                {
                                    hci_event_flag = ~hci_event.HCI_FLAG_CMD_COMPLETE;//Set flag
                                    WriteSerial("HCI Command Complete Failed - OGF: 0x" + (IntInBuffer[4] >> 2) + " OCF: 0x" + IntInBuffer[3]);
                                    return;
                                }

                                hci_event_flag |= hci_event.HCI_FLAG_CMD_COMPLETE;//Set flag                                

                                // parameters from read local bluetooth address
                                if ((IntInBuffer[3] == 0x09) && (IntInBuffer[4] == 0x10))
                                {
                                    for (byte n = 0; n < 6; n++)
                                        my_bdaddr[n] = IntInBuffer[6 + n];
                                    //Debug.Print("Read Local Bluetooth Address: " + ByteToHex(my_bdaddr[5]).ToString() + ":" + ByteToHex(my_bdaddr[4]).ToString() + ":" + ByteToHex(my_bdaddr[3]).ToString() + ":" + ByteToHex(my_bdaddr[2]).ToString() + ":" + ByteToHex(my_bdaddr[1]).ToString() + ":" + ByteToHex(my_bdaddr[0]).ToString());
                                }
                                break;

                            case EV_COMMAND_STATUS:
                                //Debug.Print("Command Status");
                                hci_command_packets = IntInBuffer[3];// update flow control
                                hci_event_flag |= hci_event.HCI_FLAG_CMD_STATUS;

                                if (IntInBuffer[2] == 0)
                                    Debug.Print("Command Status - Complete: 0x" + ByteToHex(IntInBuffer[5]) + ByteToHex(IntInBuffer[4]));
                                else// show status on serial if not OK
                                    WriteSerial("HCI Command Failed - Error: 0x" + ByteToHex(IntInBuffer[2]) + " Command: 0x" + ByteToHex(IntInBuffer[5]) + ByteToHex(IntInBuffer[4]));
                                break;

                            case EV_CONNECT_COMPLETE:
                                if (IntInBuffer[2] == 0)// check if connected OK
                                {
                                    hci_handle = IntInBuffer[3] | IntInBuffer[4] << 8; //store the handle for the ACL connection
                                    Debug.Print("Connect Complete - HCI Handle: " + hci_handle);                                    
                                    hci_event_flag |= hci_event.HCI_FLAG_CONN_COMPLETE; //set connection OK flag                                                                          
                                }
                                break;

                            case EV_DISCONNECT_COMPLETE:
                                if (IntInBuffer[2] == 0)// check if disconnected OK
                                {
                                    string reason = "Unknown Reason";
                                    if (IntInBuffer[5] == 0x08)
                                        reason = "Connection Timeout";
                                    else if (IntInBuffer[5] == 0x13)
                                        reason = "Remote User Terminated Connection";
                                    else if (IntInBuffer[5] == 0x14)
                                        reason = "Remote Device Terminated Connection due to Low Resources";
                                    else if (IntInBuffer[5] == 0x15)
                                        reason = "Remote Device Terminated Connection due to Power Off";
                                    else if (IntInBuffer[5] == 0x16)
                                        reason = "Connection Terminated By Local Host";

                                    Debug.Print("Disconnect Complete - Reason: " + reason);

                                    hci_event_flag |= hci_event.HCI_FLAG_DISCONN_COMPLETE;
                                    hci_event_flag &= ~(hci_event.HCI_FLAG_CONN_COMPLETE); //clear connection OK flag                                    
                                }
                                break;

                            case EV_NUM_COMPLETE_PKT:
                                //Debug.Print("Number Of Completed Packets: " + (uint)(IntInBuffer[6] | IntInBuffer[7] << 8));
                                break;

                            case EV_INCOMING_CONNECT:
                                //Infact it also sends the class and link type, but it only stores the bluetooth address
                                disc_bdaddr[0] = IntInBuffer[2];
                                disc_bdaddr[1] = IntInBuffer[3];
                                disc_bdaddr[2] = IntInBuffer[4];
                                disc_bdaddr[3] = IntInBuffer[5];
                                disc_bdaddr[4] = IntInBuffer[6];
                                disc_bdaddr[5] = IntInBuffer[7];

                                Debug.Print("Incoming Connection");
                                Debug.Print("Bluetooth Address: " + ByteToHex(disc_bdaddr[5]) + ":" + ByteToHex(disc_bdaddr[4]) + ":" + ByteToHex(disc_bdaddr[3]) + ":" + ByteToHex(disc_bdaddr[2]) + ":" + ByteToHex(disc_bdaddr[1]) + ":" + ByteToHex(disc_bdaddr[0]));
                                hci_event_flag |= hci_event.HCI_FLAG_INCOMING_REQUEST;
                                break;

                            case EV_ROLE_CHANGED:
                                Debug.Print("Role Changed");
                                dev_role = IntInBuffer[9];
                                break;

                            default:
                                if (IntInBuffer[0] != 0x00)
                                    Debug.Print("Unmanaged event: " + ByteToHex(IntInBuffer[0]));
                                break;
                        }
                        HCI_task();//Start HCI_task
                    }
                    catch (Exception ex)
                    {
                        WriteSerial("Exception was thrown - try reconnecting the bluetooth dongle");
                        Debug.Print("==============================");
                        Debug.Print(DateTime.Now.ToString());
                        Debug.Print(ex.Message);
                        Debug.Print(ex.StackTrace);
                        if (ex.InnerException != null)
                        {
                            Debug.Print("Inner Exception: " + ex.InnerException.Message);
                        }                        
                    }
                }
            }
        }
        void HCI_task()
        {
            switch (hci_state)
            {
                case HCI_INIT_STATE:
                    hci_counter++;
                    if (hci_counter > 10)
                    {
                        // wait until we have looped 10 times to clear any old events
                        WriteSerial("Init State");
                        hci_reset();
                        hci_state = HCI_RESET_STATE;
                        hci_counter = 0;
                    }
                    break;

                case HCI_RESET_STATE:
                    hci_counter++;
                    if (hciflag(hci_event.HCI_FLAG_CMD_COMPLETE))
                    {
                        WriteSerial("HCI Reset Complete");                        
                        hci_state = HCI_BDADDR_STATE;
                        hci_read_bdaddr();
                        hci_counter = 0;
                    }
                    if (hci_counter > 100)
                    {
                        WriteSerial("No Response to HCI Reset - Try reconnecting the Bluetooth Dongle");
                        hci_state = HCI_INIT_STATE;
                        hci_counter = 0;
                    }
                    break;

                case HCI_BDADDR_STATE:
                    if (hciflag(hci_event.HCI_FLAG_CMD_COMPLETE))
                    {
                        WriteSerial("Local Bluetooth Address: " + ByteToHex(my_bdaddr[5]).ToString() + ":" + ByteToHex(my_bdaddr[4]).ToString() + ":" + ByteToHex(my_bdaddr[3]).ToString() + ":" + ByteToHex(my_bdaddr[2]).ToString() + ":" + ByteToHex(my_bdaddr[1]).ToString() + ":" + ByteToHex(my_bdaddr[0]).ToString());
                        BDaddr[0] = my_bdaddr[5];//The commands are sent as LSB
                        BDaddr[1] = my_bdaddr[4];
                        BDaddr[2] = my_bdaddr[3];
                        BDaddr[3] = my_bdaddr[2];
                        BDaddr[4] = my_bdaddr[1];
                        BDaddr[5] = my_bdaddr[0];

                        hci_state = HCI_SCANNING_STATE;
                    }
                    break;

                case HCI_SCANNING_STATE:
                    WriteSerial("Wait For Incoming Connection Request");
                    hci_write_scan_enable();
                    hci_state = HCI_CONNECT_IN_STATE;
                    break;

                case HCI_CONNECT_IN_STATE:
                    if (hciflag(hci_event.HCI_FLAG_INCOMING_REQUEST))
                    {
                        WriteSerial("Incoming Request");
                        hci_remote_name(0);
                        hci_state = HCI_REMOTE_NAME;
                    }
                    break;

                case HCI_REMOTE_NAME:
                    if (hciflag(hci_event.HCI_FLAG_REMOTE_NAME))
                    {
                        WriteSerial("Remote Name:");
                        byte i;
                        for (i = 0; i < 40; i++)
                        {
                            if (remote_name[i] == 0x00)
                                break;                                                                                
                        }
                        remote_name[i] = 13;// Carriage Return
                        remote_name[i + 1] = 10;// Line Feed 

                        UART.Write(remote_name, 0, i + 2);
                        hci_accept_connection();
                        hci_state = HCI_CONNECTED_STATE;
                    }
                    break;

                case HCI_CONNECTED_STATE:
                    if (hciflag(hci_event.HCI_FLAG_CONN_COMPLETE))
                    {
                        WriteSerial("Connected to Device: " + ByteToHex(disc_bdaddr[5]) + ":" + ByteToHex(disc_bdaddr[4]) + ":" + ByteToHex(disc_bdaddr[3]) + ":" + ByteToHex(disc_bdaddr[2]) + ":" + ByteToHex(disc_bdaddr[1]) + ":" + ByteToHex(disc_bdaddr[0]));
                        hci_write_scan_disable();//Only allow one controller
                        hci_state = HCI_DISABLE_SCAN;
                    }
                    break;

                case HCI_DISABLE_SCAN:
                    if (hciflag(hci_event.HCI_FLAG_CMD_COMPLETE))
                    {                            
                        WriteSerial("Scan Disabled");
                        l2cap_state = L2CAP_EV_CONTROL_SETUP;
                        hci_state = HCI_DONE_STATE;
                    }
                    break;

                case HCI_DONE_STATE:
                    if (hciflag(hci_event.HCI_FLAG_DISCONN_COMPLETE))
                        hci_state = HCI_DISCONNECT_STATE;
                    break;

                case HCI_DISCONNECT_STATE:
                    if (hciflag(hci_event.HCI_FLAG_DISCONN_COMPLETE))
                    {
                        WriteSerial("Disconnected from Device: " + ByteToHex(disc_bdaddr[5]) + ":" + ByteToHex(disc_bdaddr[4]) + ":" + ByteToHex(disc_bdaddr[3]) + ":" + ByteToHex(disc_bdaddr[2]) + ":" + ByteToHex(disc_bdaddr[1]) + ":" + ByteToHex(disc_bdaddr[0]));
                        l2cap_event_status = 0;//Clear all flags
                        hci_event_flag = 0;//Clear all flags 
                        
                        //Reset all buffers                        
                        for (byte i = 0; i < IntInBuffer.Length; i++)
                            IntInBuffer[i] = 0;                        
                        for (byte i = 0; i < HCIBuffer.Length; i++)
                            HCIBuffer[i] = 0;
                        for (byte i = 0; i < BulkInBuffer.Length; i++)
                            BulkInBuffer[i] = 0;
                        for (byte i = 0; i < L2CAPBuffer.Length; i++)
                            L2CAPBuffer[i] = 0;
                        for (int i = 0; i < PS3Controller.OUTPUT_REPORT_BUFFER.Length; i++)//First two bytes reserved for report type and ID
                            HIDBuffer[i + 2] = PS3Controller.OUTPUT_REPORT_BUFFER[i];
                        for (int i = 2; i < HIDMoveBuffer.Length; i++)//First two bytes reserved for DATA request and ID
                            HIDMoveBuffer[i] = 0;
                        
                        l2cap_state = L2CAP_EV_WAIT;                        
                        hci_state = HCI_SCANNING_STATE;
                    }
                    break;
                default:
                    break;

            }
        }

        private void BulkReadingThread()
        {
            while (true)
            {
                //Read every bInterval
                Thread.Sleep(BulkInPipe.PipeEndpoint.bInterval);                
                {
                    try
                    {
                        BulkInPipe.TransferData(BulkInBuffer, 0, BulkInBuffer.Length);
                        // uncomment for debugging
                        /*
                        int i = 0;
                        WriteSerial(                            
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                            ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++])
                            );
                        */
                        if (((BulkInBuffer[0] | (BulkInBuffer[1] << 8)) == (hci_handle | 0x2000)))//acl_handle_ok
                        {                            
                            if ((BulkInBuffer[6] | (BulkInBuffer[7] << 8)) == 0x0001)//l2cap_control - Channel ID for ACL-U                                
                            {
                                if (BulkInBuffer[8] != 0x00)
                                    Debug.Print("L2CAP Signaling Command - 0x" + ByteToHex(BulkInBuffer[8]));
                                
                                if (BulkInBuffer[8] == L2CAP_CMD_COMMAND_REJECT)                                
                                    Debug.Print("L2CAP Command Reject - Reason: " + ByteToHex(BulkInBuffer[13]) + ByteToHex(BulkInBuffer[12]) + " Data: " + ByteToHex(BulkInBuffer[17]) + " " + ByteToHex(BulkInBuffer[16]) + " " + ByteToHex(BulkInBuffer[15]) + " " + ByteToHex(BulkInBuffer[14]));                                
                                else if (BulkInBuffer[8] == L2CAP_CMD_CONNECTION_REQUEST)
                                {
                                    //Debug.Print("Code: 0x" + ByteToHex(BulkInBuffer[8]) + " Identifier: 0x" + ByteToHex(BulkInBuffer[9]) + " Length: 0x" + ByteToHex(BulkInBuffer[11]) + ByteToHex(BulkInBuffer[10]) + " PSM: 0x" + ByteToHex(BulkInBuffer[13]) + ByteToHex(BulkInBuffer[12]) + " SCID: 0x" + ByteToHex(BulkInBuffer[15]) + ByteToHex(BulkInBuffer[14]));                                    
                                    Debug.Print("L2CAP Connection Request - Source CID: 0x" + ByteToHex(BulkInBuffer[15]) + ByteToHex(BulkInBuffer[14]) + " PSM: 0x" + ByteToHex(BulkInBuffer[13]) + ByteToHex(BulkInBuffer[12]));
                                    if ((BulkInBuffer[13] | BulkInBuffer[12]) == L2CAP_PSM_HID_CTRL)
                                    {
                                        identifier = BulkInBuffer[9];
                                        control_scid[0] = BulkInBuffer[14];
                                        control_scid[1] = BulkInBuffer[15];
                                        l2cap_event_status |= l2cap_event.L2CAP_EV_CONTROL_CONNECTION_REQUEST;
                                    }
                                    else if ((BulkInBuffer[13] | BulkInBuffer[12]) == L2CAP_PSM_HID_INTR)
                                    {
                                        identifier = BulkInBuffer[9];
                                        interrupt_scid[0] = BulkInBuffer[14];
                                        interrupt_scid[1] = BulkInBuffer[15];
                                        l2cap_event_status |= l2cap_event.L2CAP_EV_INTERRUPT_CONNECTION_REQUEST;
                                    }
                                }
                                else if (BulkInBuffer[8] == L2CAP_CMD_CONFIG_RESPONSE)
                                {
                                    if (BulkInBuffer[12] == control_dcid[0] && BulkInBuffer[13] == control_dcid[1])
                                    {
                                        if ((BulkInBuffer[16] | (BulkInBuffer[17] << 8)) == 0x0000)//Success
                                        {
                                            Debug.Print("HID Control Configuration Complete");
                                            l2cap_event_status |= l2cap_event.L2CAP_EV_CONTROL_CONFIG_SUCCESS;
                                        }
                                    }
                                    else if (BulkInBuffer[12] == interrupt_dcid[0] && BulkInBuffer[13] == interrupt_dcid[1])
                                    {
                                        if ((BulkInBuffer[16] | (BulkInBuffer[17] << 8)) == 0x0000)//Success
                                        {
                                            Debug.Print("HID Interrupt Configuration Complete");
                                            l2cap_event_status |= l2cap_event.L2CAP_EV_INTERRUPT_CONFIG_SUCCESS;
                                        }
                                    }
                                }
                                else if (BulkInBuffer[8] == L2CAP_CMD_CONFIG_REQUEST)
                                {
                                    if (BulkInBuffer[12] == control_dcid[0] && BulkInBuffer[13] == control_dcid[1])
                                    {
                                        Debug.Print("HID Control Configuration Request");
                                        identifier = BulkInBuffer[9]; 
                                        l2cap_event_status |= l2cap_event.L2CAP_EV_CONTROL_CONFIG_REQUEST;
                                    }
                                    else if (BulkInBuffer[12] == interrupt_dcid[0] && BulkInBuffer[13] == interrupt_dcid[1])
                                    {
                                        Debug.Print("HID Interrupt Configuration Request");
                                        identifier = BulkInBuffer[9];
                                        l2cap_event_status |= l2cap_event.L2CAP_EV_INTERRUPT_CONFIG_REQUEST;
                                    }
                                }                                    
                                else if (BulkInBuffer[8] == L2CAP_CMD_DISCONNECT_REQUEST)
                                {
                                    if (BulkInBuffer[12] == control_dcid[0] && BulkInBuffer[13] == control_dcid[1])
                                        Debug.Print("Disconnected Request: Disconnected Control");

                                    else if (BulkInBuffer[12] == interrupt_dcid[0] && BulkInBuffer[13] == interrupt_dcid[1])
                                        Debug.Print("Disconnected Request: Disconnected Interrupt");                                                                                
                                }
                                else if (BulkInBuffer[8] == L2CAP_CMD_DISCONNECT_RESPONSE)
                                {
                                    if (BulkInBuffer[12] == control_scid[0] && BulkInBuffer[13] == control_scid[1])
                                    {                                        
                                        Debug.Print("Disconnected Response: Disconnected Control");
                                        identifier = BulkInBuffer[9];
                                        l2cap_event_status |= l2cap_event.L2CAP_EV_CONTROL_DISCONNECT_RESPONSE;
                                    }

                                    else if (BulkInBuffer[12] == interrupt_scid[0] && BulkInBuffer[13] == interrupt_scid[1])
                                    {                                        
                                        Debug.Print("Disconnected Response: Disconnected Interrupt");
                                        identifier = BulkInBuffer[9];
                                        l2cap_event_status |= l2cap_event.L2CAP_EV_INTERRUPT_DISCONNECT_RESPONSE;                                        
                                    }
                                }                                     
                            }                                
                            else if (BulkInBuffer[6] == interrupt_dcid[0] && BulkInBuffer[7] == interrupt_dcid[1])//l2cap_interrupt
                            {                                
                                //Debug.Print("L2CAP Interrupt");                                
                                //readReport();//Uncomment for debugging
                            }
                            L2CAP_task();
                        }

                    }
                    catch (Exception ex)
                    {
                        WriteSerial("Exception was thrown - try reconnecting the bluetooth dongle");
                        Debug.Print("==============================");
                        Debug.Print(DateTime.Now.ToString());
                        Debug.Print(ex.Message);
                        Debug.Print(ex.StackTrace);
                        if (ex.InnerException != null)
                        {
                            Debug.Print("Inner Exception: " + ex.InnerException.Message);
                        }                        
                    }                    
                }
            }
        }
        void L2CAP_task()
        {
            switch (l2cap_state)
            {
                case L2CAP_EV_WAIT:
                    break;
                case L2CAP_EV_CONTROL_SETUP:
                    if (l2capflag(l2cap_event.L2CAP_EV_CONTROL_CONNECTION_REQUEST))
                    {
                        WriteSerial("HID Control Incoming Connection Request");
                        l2cap_connection_response(identifier, control_dcid, control_scid, PENDING);                        
                        l2cap_connection_response(identifier, control_dcid, control_scid, SUCCESSFUL);                        
                        identifier++;
                        l2cap_config_request(identifier, control_scid);                        

                        l2cap_state = L2CAP_EV_CONTROL_REQUEST;
                    }
                    break;
                case L2CAP_EV_CONTROL_REQUEST:
                    if (l2capflag(l2cap_event.L2CAP_EV_CONTROL_CONFIG_REQUEST))
                    {
                        WriteSerial("HID Control Configuration Request");
                        l2cap_config_response(identifier, control_scid);                        
                        l2cap_state = L2CAP_EV_CONTROL_SUCCESS;
                    }
                    break;

                case L2CAP_EV_CONTROL_SUCCESS:
                    if (l2capflag(l2cap_event.L2CAP_EV_CONTROL_CONFIG_SUCCESS))
                    {
                        WriteSerial("HID Control Successfully Configured");
                        l2cap_state = L2CAP_EV_INTERRUPT_SETUP;
                    }
                    break;
                case L2CAP_EV_INTERRUPT_SETUP:
                    if (l2capflag(l2cap_event.L2CAP_EV_INTERRUPT_CONNECTION_REQUEST))
                    {
                        WriteSerial("HID Interrupt Incoming Connection Request");
                        l2cap_connection_response(identifier, interrupt_dcid, interrupt_scid, PENDING);                        
                        l2cap_connection_response(identifier, interrupt_dcid, interrupt_scid, SUCCESSFUL);                        
                        identifier++;
                        l2cap_config_request(identifier, interrupt_scid);                        

                        l2cap_state = L2CAP_EV_INTERRUPT_REQUEST;
                    }
                    break;
                case L2CAP_EV_INTERRUPT_REQUEST:
                    if (l2capflag(l2cap_event.L2CAP_EV_INTERRUPT_CONFIG_REQUEST))
                    {
                        WriteSerial("HID Interrupt Configuration Request");
                        l2cap_config_response(identifier, interrupt_scid);                        
                        l2cap_state = L2CAP_EV_INTERRUPT_SUCCESS;
                    }
                    break;
                case L2CAP_EV_INTERRUPT_SUCCESS:
                    if (l2capflag(l2cap_event.L2CAP_EV_INTERRUPT_CONFIG_SUCCESS))
                    {
                        WriteSerial("HID Interrupt Successfully Configured");
                        l2cap_state = L2CAP_EV_HID_ENABLE_SIXAXIS;
                    }
                    break;
                case L2CAP_EV_HID_ENABLE_SIXAXIS:
                    Thread.Sleep(1000);

                    if (remote_name[0] == 0x50)//First letter in PLAYSTATION(R)3 Controller ('P')
                    {
                        hid_enable_sixaxis();
                        WriteSerial("Dualshock 3 Controller Enabled");
                        hid_setLedOn(LED.LED1);
                        PS3BTConnected = true;
                        for (byte i = 15; i < 19; i++)
                            BulkInBuffer[i] = 0x7F;//Set the analog joystick values to center position
                    }
                    else if (remote_name[0] == 0x4E)//First letter in Navigation Controller ('N)
                    {
                        hid_enable_sixaxis();
                        WriteSerial("Navigation Controller Enabled");
                        PS3NavigationBTConnected = true;
                        for (byte i = 15; i < 17; i++)
                            BulkInBuffer[i] = 0x7F;//Set the analog joystick values to center
                        BulkInBuffer[12] = 0x00;//reset the 12 bytes, as the program sometimes read it as the Cross button has been pressed
                    }
                    else if (remote_name[0] == 0x4D)//First letter in Motion Controller ('M')
                    {
                        WriteSerial("Motion Controller Enabled");

                        hid_MoveSetLed(Colors.Red);
                        Thread.Sleep(100);
                        hid_MoveSetLed(Colors.Green);
                        Thread.Sleep(100);
                        hid_MoveSetLed(Colors.Blue);
                        Thread.Sleep(100);

                        hid_MoveSetLed(Colors.Yellow);
                        Thread.Sleep(100);
                        hid_MoveSetLed(Colors.Lightblue);
                        Thread.Sleep(100);
                        hid_MoveSetLed(Colors.Purble);
                        Thread.Sleep(100);

                        hid_MoveSetLed(Colors.Off);                        

                        PS3MoveBTConnected = true;
                        timerLEDRumble = DateTime.Now.Ticks;
                        BulkInBuffer[12] = 0x00;//reset the 12 bytes, as the program sometimes read it as the PS_Move button has been pressed
                    }                    
                    WriteSerial("HID Done");
                    l2cap_state = L2CAP_EV_L2CAP_DONE;                    
                    break;

                case L2CAP_EV_L2CAP_DONE:
                    if (PS3MoveBTConnected)//The LED and rumble values, has to be send at aproximatly every 5th second for it to stay on
                    {                        
                        //10.000.000 ticks is equal to 1 second
                        dtimeLEDRumble = DateTime.Now.Ticks - timerLEDRumble;
                        if (dtimeLEDRumble / 10000000 >= 4)
                        {
                            HIDMove_Command(HIDMoveBuffer, HIDMoveBuffer.Length);//The LED and rumble values, has to be written again and again, for it to stay turned on
                            timerLEDRumble = DateTime.Now.Ticks;
                        }
                    }
                    break;

                case L2CAP_EV_INTERRUPT_DISCONNECT:
                    if (l2capflag(l2cap_event.L2CAP_EV_INTERRUPT_DISCONNECT_RESPONSE))
                    {
                        identifier++;
                        l2cap_disconnection_request(identifier, control_dcid, control_scid);                                                
                        l2cap_state = L2CAP_EV_CONTROL_DISCONNECT;
                    }
                    break;

                case L2CAP_EV_CONTROL_DISCONNECT:
                    if (l2capflag(l2cap_event.L2CAP_EV_CONTROL_DISCONNECT_RESPONSE))
                    {
                        hci_disconnect();
                        l2cap_state = L2CAP_EV_L2CAP_DONE;
                        hci_state = HCI_DISCONNECT_STATE;
                    }
                    break;
            }
        }                                    
        public enum Button
        {
            // byte location | bit location

            //Sixaxis Dualshcock 3            
            SELECT = (11 << 8) | 0x01,
            L3 = (11 << 8) | 0x02,
            R3 = (11 << 8) | 0x04,
            START = (11 << 8) | 0x08,
            UP = (11 << 8) | 0x10,
            RIGHT = (11 << 8) | 0x20,
            DOWN = (11 << 8) | 0x40,
            LEFT = (11 << 8) | 0x80,

            L2 = (12 << 8) | 0x01,
            R2 = (12 << 8) | 0x02,
            L1 = (12 << 8) | 0x04,
            R1 = (12 << 8) | 0x08,
            TRIANGLE = (12 << 8) | 0x10,
            CIRCLE = (12 << 8) | 0x20,
            CROSS = (12 << 8) | 0x40,
            SQUARE = (12 << 8) | 0x80,

            PS = (13 << 8) | 0x01,

            //Playstation Move Controller
            SELECT_MOVE = (10 << 8) | 0x01,
            START_MOVE = (10 << 8) | 0x08,

            TRIANGLE_MOVE = (11 << 8) | 0x10,
            CIRCLE_MOVE = (11 << 8) | 0x20,
            CROSS_MOVE = (11 << 8) | 0x40,
            SQUARE_MOVE = (11 << 8) | 0x80,

            PS_MOVE = (12 << 8) | 0x01,
            MOVE_MOVE = (12 << 8) | 0x08,//covers 12 bits - we only need to read the top 8            
            T_MOVE = (12 << 8) | 0x10,//covers 12 bits - we only need to read the top 8            
        }
        public enum AnalogButton
        {
            //Sixaxis Dualshcock 3
            UP = 23,
            RIGHT = 24,
            DOWN = 25,
            LEFT = 26,

            L2 = 27,
            R2 = 28,
            L1 = 29,
            R1 = 30,
            TRIANGLE = 31,
            CIRCLE = 32,
            CROSS = 33,
            SQUARE = 34,

            //Playstation Move Controller
            T_MOVE = 15,//Both at byte 14 (last reading) and byte 15 (current reading)
        }
        public enum AnalogHat
        {
            LeftHatX = 15,
            LeftHatY = 16,
            RightHatX = 17,
            RightHatY = 18,
        }
        public enum Sensor
        {
            //Sensors inside the Sixaxis Dualshock 3 controller
            aX = 50,
            aY = 52,
            aZ = 54,
            gZ = 56,

            //Sensors inside the move motion controller - it only reads the 2nd frame
            aXmove = 28,
            aZmove = 30,
            aYmove = 32,

            gXmove = 40,
            gZmove = 42,
            gYmove = 44,

            tempMove = 46,

            mXmove = 47,
            mZmove = 49,
            mYmove = 50,            
        }
        public enum Angle
        {
            Pitch = 0x01,
            Roll = 0x02,
        }
        public enum Status
        {
            // byte location | bit location
            Plugged = (38 << 8) | 0x02,
            Unplugged = (38 << 8) | 0x03,

            Charging = (39 << 8) | 0xEE,
            NotCharging = (39 << 8) | 0xF1,
            Shutdown = (39 << 8) | 0x01,
            Dying = (39 << 8) | 0x02,
            Low = (39 << 8) | 0x03,
            High = (39 << 8) | 0x04,
            Full = (39 << 8) | 0x05,

            MoveCharging = (21 << 8) | 0xEE,
            MoveNotCharging = (21 << 8) | 0xF1,
            MoveShutdown = (21 << 8) | 0x01,
            MoveDying = (21 << 8) | 0x02,
            MoveLow = (21 << 8) | 0x03,
            MoveHigh = (21 << 8) | 0x04,
            MoveFull = (21 << 8) | 0x05,

            CableRumble = (40 << 8) | 0x10,//Opperating by USB and rumble is turned on
            Cable = (40 << 8) | 0x12,//Opperating by USB and rumble is turned off 
            BluetoothRumble = (40 << 8) | 0x14,//Opperating by bluetooth and rumble is turned on
            Bluetooth = (40 << 8) | 0x16,//Opperating by bluetooth and rumble is turned off                        
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
        public enum Colors
        {
            //Used to set the colors of the move controller            
            Red = (255 << 16) | (0 << 8) | 0,
            Green = (0 << 16) | (255 << 8) | 0,
            Blue = (0 << 16) | (0 << 8) | 255,

            Yellow = (255 << 16) | (235 << 8) | 4,
            Lightblue = (0 << 16) | (255 << 8) | 255,
            Purble = (255 << 16) | (0 << 8) | 255,

            Off = (0 << 16) | (0 << 8) | 0,
        }
        public bool GetButton(Button b)
        {
            if (BulkInBuffer == null)
                return false;
            if ((BulkInBuffer[(uint)b >> 8] & ((byte)b & 0xff)) > 0)
                return true;
            else
                return false;
        }
        public byte GetAnalogButton(AnalogButton a)
        {
            if (BulkInBuffer == null)
                return 0;
            return (byte)(BulkInBuffer[(uint)a]);
        }
        public byte GetAnalogHat(AnalogHat a)
        {
            if (BulkInBuffer == null)
                return 0;                        
            return (byte)(BulkInBuffer[(uint)a]);            
        }
        public long GetSensor(Sensor a)
        {
            if (a == Sensor.aX || a == Sensor.aY || a == Sensor.aZ || a == Sensor.gZ)
            {
                if (BulkInBuffer == null)
                    return 0;
                return ((BulkInBuffer[(uint)a] << 8) | BulkInBuffer[(uint)a + 1]);
            }
            else if (a == Sensor.mXmove || a == Sensor.mYmove || a == Sensor.mZmove)
            {
                //Might not be correct, haven't tested it yet
                if (BulkInBuffer == null)
                    return 0;
                if (a == Sensor.mXmove)
                    return ((BulkInBuffer[(uint)a + 1] << 0x04) | (BulkInBuffer[(uint)a] << 0x0C));
                    //return ((BulkInBuffer[(uint)a + 1]) | ((BulkInBuffer[(uint)a] & 0x0F)) << 8);
                else if (a == Sensor.mYmove)
                    return ((BulkInBuffer[(uint)a + 1] & 0xF0) | (BulkInBuffer[(uint)a] << 0x08));
                    //return ((BulkInBuffer[(uint)a + 1]) | ((BulkInBuffer[(uint)a] & 0x0F)) << 8);
                else if (a == Sensor.mZmove)
                    return ((BulkInBuffer[(uint)a + 1] << 0x0F) | (BulkInBuffer[(uint)a] << 0x0C));
                    //return (((BulkInBuffer[(uint)a + 1] & 0xF0) >> 4) | (BulkInBuffer[(uint)a] << 4));
                else
                    return 0;                
            }
            else if (a == Sensor.tempMove)
            {
                if (BulkInBuffer == null)
                    return 0;
                return (((BulkInBuffer[(uint)a + 1] & 0xF0) >> 4) | (BulkInBuffer[(uint)a] << 4));                
            }
            else
            {
                if (BulkInBuffer == null)
                    return 0;
                return (((BulkInBuffer[(uint)a + 1] << 8) | BulkInBuffer[(uint)a]) - 0x8000);                
            }
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
            
            accXin = GetSensor(Sensor.aX);
            accXval = (zeroG - accXin) / sensivity;//Convert to g's
            accXval *= 2;

            accYin = GetSensor(Sensor.aY);
            accYval = (zeroG - accYin) / sensivity;//Convert to g's
            accYval *= 2;

            accZin = GetSensor(Sensor.aZ);
            accZval = (zeroG - accZin) / sensivity;//Convert to g's
            accZval *= 2;

            //Debug.Print("accXin: " + accXin + " accYin: " + accYin + " accZin: " + accZin);
            //Debug.Print("aX: " + accXval + " aY: " + accYval + " aZ: " + accZval);

            R = MathEx.Sqrt(MathEx.Pow(accXval, 2) + MathEx.Pow(accYval, 2) + MathEx.Pow(accZval, 2));

            if (a == Angle.Pitch)
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
            if (BulkInBuffer == null)
                return false;
            if (BulkInBuffer[(uint)c >> 8] == ((byte)c & 0xff))
                return true;
            return false;
        }
        public string GetStatusString()
        {
            if (!PS3MoveBTConnected)
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
                else PowerRating = "Error: " + BulkInBuffer[39];

                string WirelessStatus = "";
                if (GetStatus(Status.CableRumble)) WirelessStatus = "Cable - Rumble is on";
                else if (GetStatus(Status.Cable)) WirelessStatus = "Cable - Rumble is off";
                else if (GetStatus(Status.BluetoothRumble)) WirelessStatus = "Bluetooth - Rumble is on";
                else if (GetStatus(Status.Bluetooth)) WirelessStatus = "Bluetooth - Rumble is off";
                else WirelessStatus = "Error";

                return ("ConnectionStatus: " + ConnectionStatus + " - PowerRating: " + PowerRating + " - WirelessStatus: " + WirelessStatus);
            }
            else
            {
                string PowerRating = "";
                if (GetStatus(Status.MoveCharging)) PowerRating = "Charging";
                else if (GetStatus(Status.MoveNotCharging)) PowerRating = "Not Charging";
                else if (GetStatus(Status.MoveShutdown)) PowerRating = "Shutdown";
                else if (GetStatus(Status.MoveDying)) PowerRating = "Dying";
                else if (GetStatus(Status.MoveLow)) PowerRating = "Low";
                else if (GetStatus(Status.MoveHigh)) PowerRating = "High";
                else if (GetStatus(Status.MoveFull)) PowerRating = "Full";
                else PowerRating = "Error: " + BulkInBuffer[21];

                return ("PowerRating: " + PowerRating);
            }            
        }
        public void disconnectController()
        {
            if (PS3BTConnected)
                PS3BTConnected = false;
            else if (PS3MoveBTConnected)
                PS3MoveBTConnected = false;
            else if (PS3NavigationBTConnected)
                PS3NavigationBTConnected = false;
                        
            //First the HID interrupt channel has to be disconencted, then the HID control channel and finally the HCI connection
            l2cap_disconnection_request(0x0A, interrupt_dcid, interrupt_scid);            
            l2cap_state = L2CAP_EV_INTERRUPT_DISCONNECT;
        }
        
        /************************************************************/
        /*             HID Report (HCI ACL Packet)                  */
        /************************************************************/
        void readReport()//Uncomment for debugging
        {
            if (BulkInBuffer[8] == 0xA1)//HID_THDR_DATA_INPUT
            {                                
                int i = 10;
                WriteSerial(
                    ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                    ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                    ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                    ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                    ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                    ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +                    
                    ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                    ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                    ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                    ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +
                    ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " +                                                         
                    ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++]) + " " + ByteToHex(BulkInBuffer[i++])                                                          
                    );                 
            }
        }

        /*                          HCI ACL Data Packet
        *
        *   buf[0]          buf[1]          buf[2]          buf[3]
        *   0       4       8    11 12      16              24            31 MSB
        *  .-+-+-+-+-+-+-+-|-+-+-+-|-+-|-+-|-+-+-+-+-+-+-+-|-+-+-+-+-+-+-+-.
        *  |      HCI Handle       |PB |BC |       Data Total Length       |   HCI ACL Data Packet
        *  .-+-+-+-+-+-+-+-|-+-+-+-|-+-|-+-|-+-+-+-+-+-+-+-|-+-+-+-+-+-+-+-.
        *
        *   buf[4]          buf[5]          buf[6]          buf[7]
        *   0               8               16                            31 MSB
        *  .-+-+-+-+-+-+-+-|-+-+-+-+-+-+-+-|-+-+-+-+-+-+-+-|-+-+-+-+-+-+-+-.
        *  |            Length             |          Channel ID           |   Basic L2CAP header
        *  .-+-+-+-+-+-+-+-|-+-+-+-+-+-+-+-|-+-+-+-+-+-+-+-|-+-+-+-+-+-+-+-.
        *
        *   buf[8]          buf[9]          buf[10]         buf[11]
        *   0               8               16                            31 MSB
        *  .-+-+-+-+-+-+-+-|-+-+-+-+-+-+-+-|-+-+-+-+-+-+-+-|-+-+-+-+-+-+-+-.
        *  |     Code      |  Identifier   |            Length             |   Control frame (C-frame)
        *  .-+-+-+-+-+-+-+-|-+-+-+-+-+-+-+-|-+-+-+-+-+-+-+-|-+-+-+-+-+-+-+-.   (signaling packet format)
        */

        /************************************************************/
        /*                    HID Commands                          */
        /************************************************************/

        //Playstation Sixaxis Dualshoch Controller commands
        private void HID_Command(byte[] data, int length)
        {
            byte[] buf = new byte[64];
            buf[0] = (byte)(hci_handle & 0xff);    // HCI handle with PB,BC flag
            buf[1] = (byte)(((hci_handle >> 8) & 0x0f) | 0x20);
            buf[2] = (byte)((4 + length) & 0xff); // HCI ACL total data length
            buf[3] = (byte)((4 + length) >> 8);
            buf[4] = (byte)(length & 0xff); // L2CAP header: Length
            buf[5] = (byte)(length >> 8);
            buf[6] = control_scid[0];
            buf[7] = control_scid[1];

            for (uint i = 0; i < length; i++)//L2CAP C-frame            
                buf[8 + i] = data[i];

            dtimeHID = DateTime.Now.Ticks - timerHID;

            if (dtimeHID / 10000 <= 250)// Check if is has been more than 250ms since last command                
                Thread.Sleep((int)(250 - dtimeHID / 10000));//There have to be a delay between commands

            BulkOutPipe.TransferData(buf, 0, length + 8);            
            timerHID = DateTime.Now.Ticks;
        }
        public void hid_setAllOff()
        {
            for (int i = 0; i < PS3Controller.OUTPUT_REPORT_BUFFER.Length; i++)
                HIDBuffer[i + 2] = PS3Controller.OUTPUT_REPORT_BUFFER[i];//First two bytes reserved for report type and ID

            HID_Command(HIDBuffer, PS3Controller.OUTPUT_REPORT_BUFFER.Length + 2);
        }
        public void hid_setRumbleOff()
        {
            HIDBuffer[3] = 0x00;
            HIDBuffer[4] = 0x00;//low mode off
            HIDBuffer[5] = 0x00;
            HIDBuffer[6] = 0x00;//high mode off

            HID_Command(HIDBuffer, PS3Controller.OUTPUT_REPORT_BUFFER.Length + 2);
        }
        public void hid_setRumbleOn(Rumble a)
        {
            /*Still not totally sure how it works, maybe something like this instead?
             * 3 - duration_right
             * 4 - power_right
             * 5 - duration_left
             * 6 - power_left
             */
            if (((uint)a & 0x30) > 0)
            {
                HIDBuffer[3] = 0xfe;
                HIDBuffer[5] = 0xfe;

                if (((uint)a & 0x10) > 0)
                {
                    HIDBuffer[4] = 0;//low mode off
                    HIDBuffer[6] = 0xff;//high mode on
                }
                else
                {
                    HIDBuffer[6] = 0;//high mode off
                    HIDBuffer[4] = 0xff;//low mode on
                }

                HID_Command(HIDBuffer, PS3Controller.OUTPUT_REPORT_BUFFER.Length + 2);
            }
        }
        public void hid_setLedOff(LED a)
        {
            //check if LED is already off
            if ((byte)((byte)(((uint)a << 1) & HIDBuffer[11])) != 0)
            {
                //set the LED into the write buffer
                HIDBuffer[11] = (byte)((byte)(((uint)a & 0x0f) << 1) ^ HIDBuffer[11]);

                HID_Command(HIDBuffer, PS3Controller.OUTPUT_REPORT_BUFFER.Length + 2);
            }            
        }
        public void hid_setLedOn(LED a)
        {
            HIDBuffer[11] = (byte)((byte)(((uint)a & 0x0f) << 1) | HIDBuffer[11]);

            HID_Command(HIDBuffer, PS3Controller.OUTPUT_REPORT_BUFFER.Length + 2);            
        }
        void hid_enable_sixaxis()
        {
            byte[] cmd_buf = new byte[12];
            cmd_buf[0] = 0x53;// HID BT Set_report (0x50) | Report Type (Feature 0x03)
            cmd_buf[1] = 0xF4;// Report ID
            cmd_buf[2] = 0x42;// Special PS3 Controller enable commands
            cmd_buf[3] = 0x03;
            cmd_buf[4] = 0x00;
            cmd_buf[5] = 0x00;

            HID_Command(cmd_buf, 6);
        }

        //Playstation Move Controller commands
        private void HIDMove_Command(byte[] data, int length)
        {
            byte[] buf = new byte[64];
            buf[0] = (byte)(hci_handle & 0xff);    // HCI handle with PB,BC flag
            buf[1] = (byte)(((hci_handle >> 8) & 0x0f) | 0x20);
            buf[2] = (byte)((4 + length) & 0xff); // HCI ACL total data length
            buf[3] = (byte)((4 + length) >> 8);
            buf[4] = (byte)(length & 0xff); // L2CAP header: Length
            buf[5] = (byte)(length >> 8);
            buf[6] = interrupt_scid[0];//The Move controller sends it's data via the intterrupt channel
            buf[7] = interrupt_scid[1];

            for (uint i = 0; i < length; i++)//L2CAP C-frame            
                buf[8 + i] = data[i];

            dtimeHID = DateTime.Now.Ticks - timerHID;

            if (dtimeHID / 10000 <= 250)// Check if is has been less than 200ms since last command                            
                Thread.Sleep((int)(250 - dtimeHID / 10000));//There have to be a delay between commands
            

            BulkOutPipe.TransferData(buf, 0, length + 8);            
            timerHID = DateTime.Now.Ticks;
        }
        public void hid_MoveSetLed(byte r, byte g, byte b)
        {            
            //set the LED's values into the write buffer            
            HIDMoveBuffer[3] = r;
            HIDMoveBuffer[4] = g;
            HIDMoveBuffer[5] = b;

            HIDMove_Command(HIDMoveBuffer, HIDMoveBuffer.Length);   
        }
        public void hid_MoveSetLed(Colors color)
        {
            //set the LED's values into the write buffer            
            HIDMoveBuffer[3] = (byte)((int)color >> 16);
            HIDMoveBuffer[4] = (byte)((int)color >> 8);
            HIDMoveBuffer[5] = (byte)(color);

            HIDMove_Command(HIDMoveBuffer, HIDMoveBuffer.Length);
        }
        public void hid_MoveSetRumble(byte rumble)
        {
            //set the rumble value into the write buffer
            HIDMoveBuffer[7] = rumble;

            HIDMove_Command(HIDMoveBuffer, HIDMoveBuffer.Length);            
        }

        /************************************************************/
        /*                    L2CAP Commands                        */
        /************************************************************/
        void L2CAP_Command(byte[] data, int length)
        {
            byte[] buf = new byte[64];
            buf[0] = (byte)(hci_handle & 0xff);    // HCI handle with PB,BC flag
            buf[1] = (byte)(((hci_handle >> 8) & 0x0f) | 0x20);
            buf[2] = (byte)((4 + length) & 0xff);   // HCI ACL total data length
            buf[3] = (byte)((4 + length) >> 8);
            buf[4] = (byte)(length & 0xff);         // L2CAP header: Length
            buf[5] = (byte)(length >> 8);
            buf[6] = 0x01;  // L2CAP header: Channel ID
            buf[7] = 0x00;  // L2CAP Signalling channel over ACL-U logical link
            
            for (uint i = 0; i < length; i++)//L2CAP C-frame
                buf[8 + i] = data[i];

            try
            {
                BulkOutPipe.TransferData(buf, 0, length + 8);
            }
            catch
            {
                //Debug.Print("L2CAP_Command failed");
                WriteSerial("L2CAP_Command failed");
            }
        }
        void l2cap_connection_response(byte rxid, byte[] dcid, byte[] scid, byte result)
        {            
            L2CAPBuffer[0] = L2CAP_CMD_CONNECTION_RESPONSE;// Code
            L2CAPBuffer[1] = rxid;// Identifier
            L2CAPBuffer[2] = 0x08;// Length
            L2CAPBuffer[3] = 0x00;
            L2CAPBuffer[4] = dcid[0];// Destination CID
            L2CAPBuffer[5] = dcid[1];
            L2CAPBuffer[6] = scid[0];// Source CID
            L2CAPBuffer[7] = scid[1];
            L2CAPBuffer[8] = result;// Result: Pending or Success
            L2CAPBuffer[9] = 0x00;
            L2CAPBuffer[10] = 0x00;//No further information
            L2CAPBuffer[11] = 0x00;

            L2CAP_Command(L2CAPBuffer, 12);            
        }        
        void l2cap_config_request(byte rxid, byte[] dcid)
        {            
            L2CAPBuffer[0] = L2CAP_CMD_CONFIG_REQUEST;// Code
            L2CAPBuffer[1] = rxid;// Identifier
            L2CAPBuffer[2] = 0x08;// Length
            L2CAPBuffer[3] = 0x00;
            L2CAPBuffer[4] = dcid[0];// Destination CID
            L2CAPBuffer[5] = dcid[1];
            L2CAPBuffer[6] = 0x00;// Flags
            L2CAPBuffer[7] = 0x00;
            L2CAPBuffer[8] = 0x01;// Config Opt: type = MTU (Maximum Transmission Unit) - Hint
            L2CAPBuffer[9] = 0x02;// Config Opt: length            
            L2CAPBuffer[10] = 0xFF;// MTU
            L2CAPBuffer[11] = 0xFF;

            L2CAP_Command(L2CAPBuffer, 12);
        }
        void l2cap_config_response(byte rxid, byte[] scid)
        {            
            L2CAPBuffer[0] = L2CAP_CMD_CONFIG_RESPONSE;// Code
            L2CAPBuffer[1] = rxid;// Identifier
            L2CAPBuffer[2] = 0x0A;// Length
            L2CAPBuffer[3] = 0x00;
            L2CAPBuffer[4] = scid[0];// Source CID
            L2CAPBuffer[5] = scid[1];
            L2CAPBuffer[6] = 0x00;// Flag
            L2CAPBuffer[7] = 0x00;
            L2CAPBuffer[8] = 0x00;// Result
            L2CAPBuffer[9] = 0x00;
            L2CAPBuffer[10] = 0x01;// Config
            L2CAPBuffer[11] = 0x02;
            L2CAPBuffer[12] = 0xA0;
            L2CAPBuffer[13] = 0x02;

            L2CAP_Command(L2CAPBuffer, 14);
        }
        void l2cap_disconnection_request(byte rxid, byte[] dcid, byte[] scid)
        {
            L2CAPBuffer[0] = L2CAP_CMD_DISCONNECT_REQUEST;// Code
            L2CAPBuffer[1] = rxid;// Identifier
            L2CAPBuffer[2] = 0x04;// Length
            L2CAPBuffer[3] = 0x00;
            L2CAPBuffer[4] = scid[0];// Really Destination CID
            L2CAPBuffer[5] = scid[1];
            L2CAPBuffer[6] = dcid[0];// Really Source CID
            L2CAPBuffer[7] = dcid[1];
            L2CAP_Command(L2CAPBuffer, 8);
        }
        /************************************************************/
        /*                    HCI Commands                        */
        /************************************************************/
        void HCI_Command(byte[] buf, int nbytes)
        {
            hci_command_packets--;
            hci_event_flag &= ~(hci_event.HCI_FLAG_CMD_COMPLETE);

            try
            {
                //Single FunctionPrimary Controller - Standard request
                raw.SendSetupTransfer(0x20, 0x00, 0x0000, 0x0000, buf, 0, nbytes);
            }
            catch
            {                
                WriteSerial("HCI_Command failed");
            }
        }
        void hci_reset()
        {
            hci_event_flag = 0; // clear all flags
            HCIBuffer[0] = 0x03;// HCI OCF = 3
            HCIBuffer[1] = 0x03 << 2; // HCI OGF = 3
            HCIBuffer[2] = 0x00; // parameter length = 0
            HCI_Command(HCIBuffer, 3);            
        }
        void hci_write_scan_enable()
        {
            HCIBuffer[0] = 0x1A; // HCI OCF = 1A
            HCIBuffer[1] = 0x03 << 2; // HCI OGF = 3
            HCIBuffer[2] = 0x01;// parameter length = 1
            HCIBuffer[3] = 0x02;// Inquiry Scan disabled. Page Scan enabled.
            HCI_Command(HCIBuffer, 4);
        }
        void hci_write_scan_disable()
        {
            HCIBuffer[0] = 0x1A; // HCI OCF = 1A
            HCIBuffer[1] = 0x03 << 2; // HCI OGF = 3
            HCIBuffer[2] = 0x01;// parameter length = 1
            HCIBuffer[3] = 0x00;// Inquiry Scan disabled. Page Scan disabled.
            HCI_Command(HCIBuffer, 4);            
        }
        void hci_read_bdaddr()
        {
            HCIBuffer[0] = 0x09; // HCI OCF = 9
            HCIBuffer[1] = 0x04 << 2; // HCI OGF = 4
            HCIBuffer[2] = 0x00; // parameter length = 0
            HCI_Command(HCIBuffer, 3);
        }
        void hci_remote_name(byte disc_device)
        {            
            hci_event_flag &= ~(hci_event.HCI_FLAG_REMOTE_NAME);//Clear flag  
            HCIBuffer[0] = 0x19; // HCI OCF = 19
            HCIBuffer[1] = 0x01 << 2; // HCI OGF = 1
            HCIBuffer[2] = 0x0A; // parameter length 10
            HCIBuffer[3] = disc_bdaddr[0]; // 6 octet bdaddr
            HCIBuffer[4] = disc_bdaddr[1];
            HCIBuffer[5] = disc_bdaddr[2];
            HCIBuffer[6] = disc_bdaddr[3];
            HCIBuffer[7] = disc_bdaddr[4];
            HCIBuffer[8] = disc_bdaddr[5];
            HCIBuffer[9] = 0x01;//Page Scan Repetition Mode
            HCIBuffer[10] = 0x00;//Reserved
            HCIBuffer[11] = 0x00;//Clock offset - low byte
            HCIBuffer[12] = 0x00;//Clock offset - high byte
            HCI_Command(HCIBuffer, 13);
        }
        void hci_accept_connection()
        {            
            hci_event_flag &= ~(hci_event.HCI_FLAG_INCOMING_REQUEST);//Clear flag  
            HCIBuffer[0] = 0x09; // HCI OCF = 9
            HCIBuffer[1] = 0x01 << 2; // HCI OGF = 1
            HCIBuffer[2] = 0x07; // parameter length 7
            HCIBuffer[3] = disc_bdaddr[0]; // 6 octet bdaddr
            HCIBuffer[4] = disc_bdaddr[1];
            HCIBuffer[5] = disc_bdaddr[2];
            HCIBuffer[6] = disc_bdaddr[3];
            HCIBuffer[7] = disc_bdaddr[4];
            HCIBuffer[8] = disc_bdaddr[5];
            HCIBuffer[9] = 0x00; //switch role to master            
            HCI_Command(HCIBuffer, 10);
            return;
        }
        void hci_disconnect()
        {
            hci_event_flag &= ~(hci_event.HCI_FLAG_DISCONN_COMPLETE);
            HCIBuffer[0] = 0x06; // HCI OCF = 6
            HCIBuffer[1] = 0x01 << 2; // HCI OGF = 1
            HCIBuffer[2] = 0x03; // parameter length = 3
            HCIBuffer[3] = (byte)(hci_handle & 0xFF);//connection handle - low byte
            HCIBuffer[4] = (byte)((hci_handle >> 8) & 0x0F);//connection handle - high byte
            HCIBuffer[5] = 0x13; // reason = Remote User Terminated Connection
            HCI_Command(HCIBuffer, 6);
        }
        
        private bool hciflag(hci_event flag)
        {
            if ((hci_event_flag & flag) == flag)
                return true;
            else
                return false;
        }
        private bool l2capflag(l2cap_event flag)
        {
            if ((l2cap_event_status & flag) == flag)
                return true;
            else
                return false;
        }
        public static string ByteToHex(byte b)
        {
            const string hex = "0123456789ABCDEF";
            int low = b & 0x0f;
            int high = b >> 4;
            string s = new string(new char[] { hex[high], hex[low] });
            return s;
        }
        static void WriteSerial(string StringToWrite)
        {            
            // convert the string to bytes
            byte[] buffer = Encoding.UTF8.GetBytes(StringToWrite + "\r\n");            
            // send the bytes on the serial port
            UART.Write(buffer, 0, buffer.Length);
            //Debug.Print(StringToWrite);
        }
    }
}
