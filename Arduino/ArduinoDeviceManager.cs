using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Solid.Arduino.Firmata;
using Solid.Arduino;
using Chetch.Services;
using Chetch.Utilities;

namespace Chetch.Arduino
{
    public enum ADMStatus
    {
        NOT_CONNECTED,
        CONNECTED,
        DEVICE_READY
    }

    public class ADMMessage : NamedPipeManager.Message
    {
        public enum Action
        {
            NOT_SET,
            SEND
        }

        public ushort Tag { get; set; } = 0; //can be used to track messages
        public ushort SenderID { get; set; } = 0; //normally set to Device ID
        public Action RequestedAction { get; set; } = 0; //request to perform a certain action
        public List<byte[]> Arguments { get; } = new List<byte[]>();
        public bool LittleEndian { get; set; } = true; //used for bytes encoding

        public ADMMessage()
        {

        }

        public override void AddBytes(List<byte> bytes)
        {
            base.AddBytes(bytes);

            bytes.AddRange(Utilities.Convert.ToBytes(Tag, LittleEndian));
            bytes.AddRange(Utilities.Convert.ToBytes(SenderID, LittleEndian));
            bytes.Add((byte)RequestedAction);

            byte delimiter = (byte)' ';
            foreach(var b in Arguments)
            {
                bytes.Add(delimiter);
                bytes.AddRange(b);
            }
        }

        public void AddArgument(UInt64 n)
        {
            AddArgument(Utilities.Convert.ToBytes(n, LittleEndian));
        }

        public void AddArgument(byte[] bytes)
        {
            Arguments.Add(bytes);
        }
    }

    public class ArduinoDeviceManager
    {
        private const int CONNECT_TIMEOUT = 10000;

        public const string ARDUINO_MEGA_2560 = "USB-SERIAL CH340";
        
        static public ArduinoDeviceManager Connect(String supportedBoards, int timeOut, Action<ADMMessage, ArduinoDeviceManager> listener)
        {
            var boards = supportedBoards.Split(',');
            foreach (var board in boards)
            {
                List<String> boardPorts = Chetch.Utilities.SerialPorts.Find(board.Trim());
                if (boardPorts.Count > 0)
                {
                    ISerialConnection connection = new EnhancedSerialConnection(boardPorts[0], SerialBaudRate.Bps_57600);
                    if (connection != null)
                    {
                        var session = new ArduinoSession(connection, timeOut);
                        try
                        {
                            var mgr = new ArduinoDeviceManager(session, listener);
                            return mgr;
                        } catch (Exception e)
                        {
                            if (connection.IsOpen) connection.Close();
                            throw e;
                        }
                    }
                }
            }

            return null;
        }

        static public ArduinoDeviceManager Connect(String supportedBoards, Action<ADMMessage, ArduinoDeviceManager> listener)
        {
            return Connect(supportedBoards, CONNECT_TIMEOUT, listener);
        }

        static public ArduinoDeviceManager Connect(String supportedBoards)
        {
            return Connect(supportedBoards, CONNECT_TIMEOUT, null);
        }


        public ADMStatus Status { get { return _status; } }
        public int DeviceCount
        {
            get
            {
                return _devices != null ? _devices.Count : 0;
            }
        }

        private ADMStatus _status;
        private ArduinoSession _session;
        private bool _littleEndian = true; //board uses little endian
        private Dictionary<String, ArduinoDevice> _devices;
        private Dictionary<String, ushort> _device2boardID;
        private BoardCapability _boardCapability;
        private Dictionary<int, List<ArduinoDevice>> _pin2device;
        private Action<ADMMessage, ArduinoDeviceManager> _listener;
        
        public ArduinoDeviceManager(ArduinoSession firmata, Action<ADMMessage, ArduinoDeviceManager> listener)
        {
            _session = firmata;
            _session.MessageReceived += OnMessageReceived;
            _listener = listener;

            _devices = new Dictionary<String, ArduinoDevice>();
            _device2boardID = new Dictionary<String, ushort>();

            //_boardCapability = _session.GetBoardCapability();
            _pin2device = new Dictionary<int, List<ArduinoDevice>>();

            _status = ADMStatus.CONNECTED;

            //if here and no exceptions then the connection should be good
            var message = new ADMMessage();
            message.Tag = 1;
            message.Type = NamedPipeManager.MessageType.STATUS_REQUEST;
            SendMessage(message);
        }

        public void Disconnect()
        {
            _status = ADMStatus.NOT_CONNECTED;
            _session.Dispose();
        }


        public void AssertConnection()
        {
            try
            {
                SendString("X");
            }
            catch (System.IO.IOException e)
            {
                _status = ADMStatus.NOT_CONNECTED;
                throw e;
            }
            catch (UnauthorizedAccessException e)
            {
                _status = ADMStatus.NOT_CONNECTED;
                throw e;
            }
        }

        public bool IsPinCapable(ArduinoPin pin)
        {
            var pinCapability = _boardCapability.Pins[pin.PinNumber];
            switch (pin.Mode)
            {
                case PinMode.AnalogInput:
                    return pinCapability.Analog;
                case PinMode.DigitalInput:
                    return pinCapability.DigitalInput;
                case PinMode.DigitalOutput:
                    return pinCapability.DigitalOutput;
                case PinMode.PwmOutput:
                    return pinCapability.Pwm;
            }

            return false;
        }

        public ArduinoDevice AddDevice(ArduinoDevice device)
        {
            if(device.ID == null)
            {
                throw new Exception("Cannot add this device as it does not have a valid ID");
            }
            if (_devices.ContainsKey(device.ID))
            {
                throw new Exception("Cannot add this device because there is already a device with ID " + device.ID);
            }
            if(device.BoardID > 0 && _device2boardID.ContainsKey(device.ID) && _device2boardID[device.ID] == device.BoardID)
            {
                throw new Exception("Cannot add this device because there is already a device using board ID " + device.BoardID);
            }

            foreach (var dpin in device.Pins)
            {
                //check that pins have the required capability
                if (!IsPinCapable(dpin)) throw new Exception("Cannot add device " + device.Name + " as pin " + dpin.PinNumber + " is not compatibl with the board");

                //check no conflict with existing pin usage
                foreach (var dev in _devices.Values)
                {
                    if (!dev.IsPinCompatible(dpin)) throw new Exception("Cannot add device " + device.Name + " as it is not pin-compatible with " + dev.Name);
                }
            }

            device.Mgr = this;
            _devices[device.ID] = device;
            if (device.BoardID > 0)
            {
                _device2boardID[device.ID] = device.BoardID;
            }

            foreach (var dpin in device.Pins)
            {
                if (!_pin2device.ContainsKey(dpin.PinNumber)) _pin2device[dpin.PinNumber] = new List<ArduinoDevice>();
                if (_pin2device[dpin.PinNumber].Contains(device)) throw new Exception("Device manager cannot contain the same device");
                _pin2device[dpin.PinNumber].Add(device);
            }


            return device;
        }

        public ArduinoDevice GetDevice(String deviceID)
        {
            if (_devices.ContainsKey(deviceID))
            {
                return _devices[deviceID];
            } else
            {
                return null;
            }
        }

        public Boolean HasDevice(String deviceID)
        {
            return GetDevice(deviceID) != null;
        }

        public List<ArduinoDevice> GetDevicesByPin(int pinNumber)
        {
            return _pin2device[pinNumber];
        }


        public void OnMessageReceived(Object sender, FirmataMessageEventArgs eventArgs)
        {
            var fmessage = eventArgs.Value;
            ADMMessage message = null;
            switch (fmessage.Type)
            {
                case MessageType.StringData:
                    StringData sd = (StringData)fmessage.Value;
                    System.Diagnostics.Debug.Print(sd.Text);
                    try
                    {
                        message = ADMMessage.Deserialize<ADMMessage>(sd.Text, NamedPipeManager.MessageEncoding.QUERY_STRING);
                    } catch (Exception e)
                    {
                        //TODO: what to do here?
                        System.Diagnostics.Debug.Print(e.Message);
                    }
                    switch (message.Type)
                    {
                        case NamedPipeManager.MessageType.STATUS_RESPONSE:
                            //TODO: check for Tag value of 1
                            _status = ADMStatus.DEVICE_READY;
                            _littleEndian = true; //TODO: get little endian from the message

                            if (!HasDevice(Diagnostics.LEDBuiltIn.LED_BUILTIN_ID))
                            {
                                var d = new Diagnostics.LEDBuiltIn(13); //TODO: get built in pin number from message
                                AddDevice(d);
                            }
                            break;

                        case NamedPipeManager.MessageType.ERROR:
                            break;
                    }
                    break;

                case MessageType.PinStateResponse:
                    break;

                case MessageType.DigitalPortState:
                    DigitalPortState state = (DigitalPortState)fmessage.Value;
                    string binary = System.Convert.ToString(state.Pins, 2);
                    break;

                case MessageType.CapabilityResponse:
                    break;

                default:
                    break;
            }

            if (_listener != null && message != null)
            {
                _listener(message, this);
            }

        }


        public void SendMessage(ADMMessage message)
        {
            if(message != null)
            {
                //SendString(message.Serialize());
            }
        }

        public void SendString(String s)
        {
            _session.SendStringData(s);
        }

        public void SetDigitalPin(int pinNumber, bool value)
        {
            _session.SetDigitalPin(pinNumber, value);
        }

        public void IssueCommand(String deviceID, String command, String[] args)
        {
            var device = GetDevice(deviceID);
            if(device == null)
            {
                throw new Exception("Cannot find device with ID " + deviceID);
            }

            device.ExecuteCommand(command, args);
        }
    }
}
