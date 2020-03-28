﻿using System;
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
        CONNECTING,
        CONNECTED,
        DEVICE_READY
    }

    public class ADMMessage : NamedPipeManager.Message
    {
        public byte Tag { get; set; } = 0; //can be used to track messages
        public byte TargetID { get; set; } = 0; //ID number on board to determine what is beig targeted
        public byte CommandID { get; set; } = 0; //Command ID on board ... basically to identify function e.g. Send or Delete ...
        public List<byte[]> Arguments { get; } = new List<byte[]>();
        
        public ADMMessage()
        {

        }

        public override void AddBytes(List<byte> bytes)
        {
            //Scheme here is 4 bytes as a 'header' to include: Type, Tag, TargetID, CommandID
            //Followed by a non-limited (although in reality it should be determined by the board)
            //number of arguments, where each argument is preceded by the number of bytes the argument needs
            base.AddBytes(bytes); //will add Type

            bytes.Add(Tag);
            bytes.Add(TargetID);
            bytes.Add(CommandID);

            foreach(var b in Arguments)
            {
                bytes.Add((byte)b.Length);
                bytes.AddRange(b);
            }

            //we now add a byte to handle the zero byte case cos the zero byte is interpreted as string termination
            byte mask = (byte)255;
            while(mask >= 1) {
                bool useable = true;
                foreach(var b in bytes)
                {
                    if(b == mask)
                    {
                        useable = false;
                        break;
                    }
                }
                if (useable) break;
                mask--;
            }

            if(mask >= 1)
            {
                bytes.Insert(0, mask); //use the 'mask' as the encoded zero byte
                for(int i = 1; i < bytes.Count; i++)
                {
                    if (bytes[i] == 0) bytes[i] = mask;
                }

            } else
            {
                throw new Exception("Unable to generate mask for byte array");
            }
        }

        public void AddArgument(byte[] bytes)
        {
            Arguments.Add(bytes);
        }

        public void AddArgument(byte b)
        {
            AddArgument(new byte[] { b });
        }

        public void AddArgument(String s)
        {
            AddArgument(Chetch.Utilities.Convert.ToBytes(s));
        }

        public String Serialize()
        {
            return Serialize(NamedPipeManager.MessageEncoding.BYTES_ARRAY);
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
        private bool _littleEndian = true; //Should be set in STATUS_RESPONSE message from board
        private String _boardName; //Should be set in STATUS_RESPONSE message from board
        private Dictionary<String, ArduinoDevice> _devices;
        private Dictionary<String, byte> _device2boardID;
        private BoardCapability _boardCapability;
        private Dictionary<int, List<ArduinoDevice>> _pin2device;
        private Action<ADMMessage, ArduinoDeviceManager> _listener;
        
        public ArduinoDeviceManager(ArduinoSession firmata, Action<ADMMessage, ArduinoDeviceManager> listener)
        {
            _status = ADMStatus.CONNECTING;

            _session = firmata;
            _session.MessageReceived += OnMessageReceived;
            _listener = listener;

            _devices = new Dictionary<String, ArduinoDevice>();
            _device2boardID = new Dictionary<String, byte>();

            _boardCapability = _session.GetBoardCapability();
            _pin2device = new Dictionary<int, List<ArduinoDevice>>();

            _status = ADMStatus.CONNECTED;

            //if here and no exceptions then the connection should be good
            //so initialise the board
            ADMMessage message = new ADMMessage();
            message.TargetID = 0;
            message.Type = NamedPipeManager.MessageType.INITIALISE;
            SendMessage(message);

            //and request board status
            RequestStatus();
        }

        public void Disconnect()
        {
            _status = ADMStatus.NOT_CONNECTED;
            _session.Dispose();
        }


        public void AssertConnection()
        {
            if(_status == ADMStatus.NOT_CONNECTED || _status == ADMStatus.CONNECTING)
            {
                throw new Exception("Status is " + _status);
            }

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
                case PinMode.Undefined:
                    return true;
            }

            return false;
        }

        public ArduinoDevice AddDevice(ArduinoDevice device)
        {
            if(_status != ADMStatus.DEVICE_READY)
            {
                throw new Exception("ADM status mut be DEVICE_READY ... currently " + _status);
            }
            if(device.ID == null)
            {
                throw new Exception("Cannot add this device as it does not have a valid ID");
            }
            if (_devices.ContainsKey(device.ID))
            {
                throw new Exception("Cannot add this device because there is already a device with ID " + device.ID);
            }

            //if no board ID is given then autogenerate one
            if(device.BoardID == 0)
            {
                if(_device2boardID.ContainsKey(device.ID))
                {
                    device.BoardID = _device2boardID[device.ID];
                } else
                {
                    if (_device2boardID.Count == 255) throw new Exception("Cannot automatically assign Board ID to device " + device.ID);
                    device.BoardID = (byte)(_device2boardID.Count + 1);
                }
            }

            if(device.BoardID > 0 && _device2boardID.ContainsKey(device.ID) && _device2boardID[device.ID] == device.BoardID)
            {
                throw new Exception("Cannot add this device because there is already a device using board ID " + device.BoardID);
            }

            foreach (var dpin in device.Pins)
            {
                //check that pins have the required capability
                if (!IsPinCapable(dpin)) throw new Exception("Cannot add device " + device.Name + " as pin " + dpin.PinNumber + " board does not support capability");

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

            //send configuration/setup data to board
            var message = new ADMMessage();
            message.Type = NamedPipeManager.MessageType.CONFIGURE;
            message.TargetID = device.BoardID;
            message.AddArgument((byte)device.Category);
            message.AddArgument(device.ID);
            message.AddArgument(device.Name);
            SendMessage(message);
            
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
                    System.Diagnostics.Debug.Print("Received: " + sd.Text);
                    try
                    {
                        message = ADMMessage.Deserialize<ADMMessage>(sd.Text, NamedPipeManager.MessageEncoding.QUERY_STRING);
                    } catch (Exception e)
                    {
                        //TODO: What to do here?
                        System.Diagnostics.Debug.Print(e.Message);
                    }
                    switch (message.Type)
                    {
                        case NamedPipeManager.MessageType.STATUS_RESPONSE:
                            if (_status != ADMStatus.DEVICE_READY)
                            {
                                _status = ADMStatus.DEVICE_READY;
                                _littleEndian = Chetch.Utilities.Convert.ToBoolean(message.GetValue("LittleEndian"));
                                _boardName = message.GetString("Board");

                                if (!HasDevice(Diagnostics.LEDBuiltIn.LED_BUILTIN_ID) && message.HasValue("LEDBuiltIn"))
                                {
                                    int pin = System.Convert.ToUInt16(message.GetValue("LEDBuiltIn"));
                                    var d = new Diagnostics.LEDBuiltIn(pin);
                                    AddDevice(d);
                                }
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

        public void SendCommand(byte targetID, ArduinoCommand command)
        {
            var message = new ADMMessage();
            message.Type = NamedPipeManager.MessageType.COMMAND;
            message.Tag = 0; //TODO: create some kind of perhaps counter-based tagging
            message.TargetID = targetID;
            message.CommandID = (byte)command.Type;

            foreach (Object arg in command.Arguments)
            {
                byte[] b;
                if (arg is String)
                {
                    b = Chetch.Utilities.Convert.ToBytes((String)arg);
                }
                else if (arg.GetType().IsValueType)
                {
                    b = Chetch.Utilities.Convert.ToBytes((ValueType)arg, _littleEndian);
                }
                else
                {
                    throw new Exception("Unable to process type " + arg.GetType());
                }
                message.AddArgument(b);
            }

            SendMessage(message);
        }

        public void SendMessage(ADMMessage message)
        {
            if(message != null)
            {
                SendString(message.Serialize());
            }
        }

        public void SendString(String s)
        {
            System.Diagnostics.Debug.Print("Sending: " + s);
            _session.SendStringData(s);
        }

        public void SetDigitalPin(int pinNumber, bool value)
        {
            _session.SetDigitalPin(pinNumber, value);
        }

        public void IssueCommand(String deviceID, String command)
        {
            var device = GetDevice(deviceID);
            if(device == null)
            {
                throw new Exception("Cannot find device with ID " + deviceID);
            }

            device.ExecuteCommand(command);
        }

        public void RequestStatus(String deviceID = null)
        {
            var message = new ADMMessage();
            message.Type = NamedPipeManager.MessageType.STATUS_REQUEST;

            if (deviceID == null)
            {
                message.TargetID = 0;
            }
            else
            {
                var d = GetDevice(deviceID);
                if (d == null)
                {
                    throw new Exception("Cannot find device with ID " + deviceID);
                }
                message.TargetID = d.BoardID;
            }
            SendMessage(message);
        }
    }
}
