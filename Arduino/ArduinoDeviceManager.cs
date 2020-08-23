using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using Solid.Arduino.Firmata;
using Solid.Arduino;
using Chetch.Services;
using Chetch.Utilities;
using Chetch.Application;
using Chetch.Messaging;

namespace Chetch.Arduino
{
    public enum ADMState
    {
        NOT_CONNECTED,
        CONNECTING,
        CONNECTED,
        DEVICE_READY,
        DEVICE_CONNECTED
    }

    public class ADMMessage : Message
    {
        public byte Tag { get; set; } = 0; //can be used to track messages
        public byte TargetID { get; set; } = 0; //ID number on board to determine what is beig targeted
        public byte CommandID { get; set; } = 0; //Command ID on board ... basically to identify function e.g. Send or Delete ...
        public List<byte[]> Arguments { get; } = new List<byte[]>();
        public bool LittleEndian { get; set; } = true;
        public bool CanBroadcast { get; set; } = true;

        public ADMMessage()
        {
            DefaultEncoding = MessageEncoding.BYTES_ARRAY;
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

        public void AddArgument(int arg)
        {
            byte[] bytes = Chetch.Utilities.Convert.ToBytes((ValueType)arg, LittleEndian);
            AddArgument(bytes);
        }
    }

    
    /*
     * ADM
     * 
     * One instance per board.
     */

    public class ArduinoDeviceManager
    {
        private const int CONNECT_TIMEOUT = 10000;
        private const int MAX_SEND_STRING_LENGTH = 32; //maximum string length to send by Firmata

        public const string ARDUINO_MEGA_2560 = "USB-SERIAL CH340";
        public const string ARDUINO_UNO = "Arduino Uno";

        public const string DEFAULT_BOARD_SET = ARDUINO_MEGA_2560 + "," + ARDUINO_UNO;

        
        static public List<String> GetBoardPorts(String supportedBoards)
        {
            if (supportedBoards == null || supportedBoards == String.Empty)
            {
                throw new Exception("ArduinoDeviceManager:Connect no supportedBoards provided");
            }

            List<String> boardPorts = SerialPorts.Find(supportedBoards.Trim());
            return boardPorts;
        }

        static public ArduinoDeviceManager Connect(String port, int timeOut, Action<ADMMessage, ArduinoDeviceManager> listener)
        {
            ISerialConnection connection = new EnhancedSerialConnection(port, SerialBaudRate.Bps_57600);
            if (connection != null)
            {
                var session = new ArduinoSession(connection, timeOut);
                try
                {
                    var mgr = new ArduinoDeviceManager(session, listener);
                    mgr.Port = port;
                    return mgr;
                } catch (Exception e)
                {
                    if (connection.IsOpen) connection.Close();
                    throw e;
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

        public TraceSource Tracing { get; set; } = null;

        private ADMState _state;
        public ADMState State {
            get { return _state; }
            internal set
            {
                if(value != _state)
                {
                    _state = value;
                }
            }
        }
        public bool IsConnected
        {
            get
            {
                return State == ADMState.CONNECTED || State == ADMState.DEVICE_READY || State == ADMState.DEVICE_CONNECTED;
            }
        }
        public int DeviceCount
        {
            get
            {
                return _devices != null ? _devices.Count : 0;
            }
        }
        public bool DevicesConnected
        {
            get
            {
                bool devicesConnected = true;
                foreach (var d in _devices.Values)
                {
                    if (!d.IsConnected)
                    {
                        devicesConnected = false;
                        break;
                    }
                }
                return devicesConnected;
            }
        }

        public String Port { get; set; }
        private ArduinoSession _session;
        public String BoardID { get; internal set; } //Should be set in STATUS_RESPONSE message from board
        public int LEDBIPin { get; internal set; } = -1; //Should be set in STATUS_RESPONSE message from board
        public bool LittleEndian { get; internal set; } = true;
        private String _boardType; //Should be set in STATUS_RESPONSE message from board

        private Dictionary<String, ArduinoDevice> _devices;
        private Dictionary<String, byte> _device2boardID;
        private Dictionary<byte, ArduinoDevice> _boardID2device;
        private BoardCapability _boardCapability;
        private Dictionary<int, List<ArduinoDevice>> _pin2device;
        private Dictionary<int, DigitalPortState> _portStates;
        private Action<ADMMessage, ArduinoDeviceManager> _listener;

        public Sampler Sampler { get; internal set; } = new Sampler();

        //Thread Locks
        private Object LockSendString = new Object();
        private Object LockSetDigitalPin = new Object();
        private Object LockSetDigitalPinMode = new Object();

        public ArduinoDeviceManager(ArduinoSession firmata, Action<ADMMessage, ArduinoDeviceManager> listener)
        {
            State = ADMState.CONNECTING;

            _devices = new Dictionary<String, ArduinoDevice>();
            _device2boardID = new Dictionary<String, byte>();
            _boardID2device = new Dictionary<byte, ArduinoDevice>();

            _pin2device = new Dictionary<int, List<ArduinoDevice>>();
            _portStates = new Dictionary<int, DigitalPortState>();

            _session = firmata;
            _listener = listener;
            _session.MessageReceived += OnMessageReceived;

            _boardCapability = _session.GetBoardCapability();
            State = ADMState.CONNECTED;

            //if here and no exceptions then the connection should be good
            //so initialise the board
            ADMMessage message = new ADMMessage();
            message.TargetID = 0;
            message.Type = Messaging.MessageType.INITIALISE;
            SendMessage(message, 250);

            //and request board status
            RequestStatus();

        }

        public void Disconnect()
        {
            State = ADMState.NOT_CONNECTED;
            Sampler.Stop();
            _session?.Dispose();
        }


        public void AssertConnection()
        {
            if(State == ADMState.NOT_CONNECTED || State == ADMState.CONNECTING)
            {
                throw new Exception("Status is " + State);
            }

            try
            {
                Ping();
            }
            catch (System.IO.IOException e)
            {
                Disconnect();
                throw e;
            }
            catch (UnauthorizedAccessException e)
            {
                Disconnect();
                throw e;
            }
        }

        public bool IsPinCapable(int pinNumber, PinMode mode)
        {
            var pinCapability = _boardCapability.Pins[pinNumber];
            switch (mode)
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

        public bool IsPinCapable(ArduinoPin pin)
        {
            return IsPinCapable(pin.PinNumber, pin.Mode);
        }

        public int GetPortForPin(int pinNumber)
        {
            return (int)Math.Floor((double)pinNumber / 8.0);
        }

        public int GetPinForPort(int portNumber, int pinIndex)
        {
            return 8 * portNumber + pinIndex;
        }

        public List<String> ListBoardCapability()
        {
            var l2r = new List<String>();
            foreach(var pc in _boardCapability.Pins)
            {
                var s = String.Format("No.: {0}, Analaog: {1}, Digital Input: {2}, Digital Output: {3}, PWM: {4}", pc.PinNumber, pc.Analog, pc.DigitalInput, pc.DigitalOutput, pc.Pwm);
                l2r.Add(s);
            }
            return l2r;
        }

        public ArduinoDevice AddDevice(ArduinoDevice device)
        {
            if(State != ADMState.DEVICE_READY)
            {
                throw new Exception("ADM state mut be DEVICE_READY ... currently " + State);
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
                _boardID2device[device.BoardID] = device;
            }

            foreach (var dpin in device.Pins)
            {
                if (!_pin2device.ContainsKey(dpin.PinNumber)) _pin2device[dpin.PinNumber] = new List<ArduinoDevice>();
                if (_pin2device[dpin.PinNumber].Contains(device)) throw new Exception("Device manager cannot contain the same device");
                _pin2device[dpin.PinNumber].Add(device);
            }

            //configure the pins on the board
            //TODO: analog pins
            foreach (var dpin in device.Pins)
            {
                switch (dpin.Mode)
                {
                    case PinMode.DigitalInput:
                    case PinMode.DigitalOutput:
                        SetDigitalPinMode(dpin.PinNumber, dpin.Mode);
                        if (dpin.InitialValue != -1)
                        {
                            SetDigitalPin(dpin.PinNumber, dpin.InitialValue > 0, 100);
                        }
                        break;
                }
            }

            //send configuration/setup data to board
            var message = new ADMMessage();
            message.LittleEndian = LittleEndian;
            message.Type = Messaging.MessageType.CONFIGURE;
            device.AddConfig(message);
            SendMessage(message, 250); //leave some time for this process to complete to avoid bombarding the board
            
            return device;
        }

        public void UpdateDevice(ArduinoDevice device)
        {
            //check pins
            foreach (var dpin in device.Pins)
            {
                //check that pins have the required capability
                if (!IsPinCapable(dpin)) throw new Exception("Cannot update device " + device.Name + " as pin " + dpin.PinNumber + " board does not support capability");

                //check no conflict with existing pin usage
                foreach (var dev in _devices.Values)
                {
                    if (dev != device && !dev.IsPinCompatible(dpin)) throw new Exception("Cannot update device " + device.Name + " as it is not pin-compatible with " + dev.Name);
                }
            }

            //maybe this was a new pin so needs to be added to the _pin2device maaping
            foreach (var dpin in device.Pins)
            {
                if (!_pin2device.ContainsKey(dpin.PinNumber)) _pin2device[dpin.PinNumber] = new List<ArduinoDevice>();
                if (!_pin2device[dpin.PinNumber].Contains(device))_pin2device[dpin.PinNumber].Add(device);
            }
        }

        public List<ArduinoDevice> GetDevices()
        {
            return _devices.Values.ToList();
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

        public ArduinoDevice GetDeviceByBoardID(byte boardID)
        {
            if (boardID > 0 && _boardID2device.ContainsKey(boardID))
            {
                return _boardID2device[boardID];
            }
            return null;
        }

        public Boolean HasDevice(String deviceID)
        {
            return GetDevice(deviceID) != null;
        }

        public List<ArduinoDevice> GetDevicesByPin(int pinNumber)
        {
            return _pin2device.ContainsKey(pinNumber) ? _pin2device[pinNumber] : null;
        }

        public ArduinoDevice GetTargetedDevice(ADMMessage message)
        {
            var boardID = Chetch.Utilities.Convert.ToByte(message.Target);
            return GetDeviceByBoardID(boardID);
        }

        public bool IsDeviceConnected(String deviceID)
        {
            var d = GetDevice(deviceID);
            return (d != null && d.IsConnected);
        }

        public void OnMessageReceived(Object sender, FirmataMessageEventArgs eventArgs)
        {
            var fmessage = eventArgs.Value;
            ADMMessage message = null;
            try
            {
                switch (fmessage.Type)
                {
                    case Solid.Arduino.Firmata.MessageType.StringData:
                        StringData sd = (StringData)fmessage.Value;
                        try
                        {
                            String serialized = sd.Text;
                            try
                            {
                                var sanitised = serialized.Replace("&", String.Empty).Replace("=", String.Empty);
                                if (!Utilities.Convert.IsUrlEncoded(sanitised))
                                {
                                    throw new Exception(String.Format("{0} is not URL encoded", sanitised));
                                }
                            }
                            catch (Exception e)
                            {
                                Tracing?.TraceEvent(TraceEventType.Error, 4000, e.Message);
                            }

                            message = ADMMessage.Deserialize<ADMMessage>(serialized, MessageEncoding.QUERY_STRING);
                            message.TargetID = Utilities.Convert.ToByte(message.Target);
                        }
                        catch (Exception e)
                        {
                            Tracing?.TraceEvent(TraceEventType.Error, 4000, "Deserializing {0} produced exception {1}: {2}", sd.Text, e.GetType(), e.Message);
                            message = new ADMMessage();
                            message.Type = Messaging.MessageType.ERROR;
                            message.Value = e.Message;
                            break;
                        }
                        switch (message.Type)
                        {
                            case Messaging.MessageType.STATUS_RESPONSE:
                                try
                                {
                                    LittleEndian = Chetch.Utilities.Convert.ToBoolean(message.GetValue("LE"));
                                    _boardType = message.GetString("BD");
                                    BoardID = message.HasValue("BDID") ? message.GetString("BDID") : null;
                                    if (State == ADMState.CONNECTED)
                                    {
                                        State = ADMState.DEVICE_READY;
                                    }

                                    if (message.HasValue("LEDBI"))
                                    {
                                        LEDBIPin = message.GetInt("LEDBI");
                                    }
                                }
                                catch (Exception e)
                                {
                                    Tracing?.TraceEvent(TraceEventType.Error, 4000, "STATUS_RESPONSE error: {0}, {1}", e.GetType(), e.Message);
                                    message = new ADMMessage();
                                    message.Type = Messaging.MessageType.ERROR;
                                    message.Value = e.Message;
                                }
                                break;

                            case Messaging.MessageType.CONFIGURE_RESPONSE:
                                break;

                            case Messaging.MessageType.ERROR:
                                break;
                        }

                        if (State == ADMState.DEVICE_READY || State == ADMState.DEVICE_CONNECTED)
                        {
                            //direct messages to devices
                            var dev = GetTargetedDevice(message);
                            if (dev != null)
                            {
                                dev.HandleMessage(message);
#if DEBUG
                                Debug.Print(String.Format("Handling message {0} for device {1} ... connected: {2}, memory: {3}", message.Type, dev.ID, dev.IsConnected, message.HasValue("FM") ? message.GetValue("FM") : "N/A"));
#endif
                            }

                            //we do this test after handling message because the message maybe a CONFIGURE_RESPONSE message which will then set the 'connected' status of the device
                            if (message.Type == Messaging.MessageType.CONFIGURE_RESPONSE && DevicesConnected)
                            {
                                State = ADMState.DEVICE_CONNECTED;
                                Sampler.Start();
                            }
                        }
                        break;

                    case Solid.Arduino.Firmata.MessageType.PinStateResponse:
                        break;

                    case Solid.Arduino.Firmata.MessageType.DigitalPortState:
                        DigitalPortState portState = (DigitalPortState)fmessage.Value;
                        int pinsChanged;
                        if (_portStates.ContainsKey(portState.Port))
                        {
                            pinsChanged = portState.Pins ^ _portStates[portState.Port].Pins;
                        }
                        else
                        {
                            pinsChanged = 255;
                        }
                        _portStates[portState.Port] = portState;

                        for (int i = 0; i < 8; i++)
                        {
                            int bit2check = (int)Math.Pow(2, i);
                            if ((bit2check & pinsChanged) == 0) continue;

                            bool state = portState.IsSet(i);
                            int pinNumber = GetPinForPort(portState.Port, i); //TODO: this might need to be board dependent
                            var devs = GetDevicesByPin(pinNumber);
                            if (devs != null)
                            {
                                foreach (var dev in devs)
                                {
                                    dev.HandleDigitalPinStateChange(pinNumber, state);
                                }
                            }
                        }

                        //String s1 = System.Convert.ToString(portState.Pins, 2);
                        //String s2 = System.Convert.ToString(pinsChanged, 2);
                        //Debug.Print("Pins/2change: " + s1 + "/" + s2);
                        break;

                    case Solid.Arduino.Firmata.MessageType.CapabilityResponse:
                        break;

                    default:
                        break;
                }
            } catch (Exception e)
            {
                message = new ADMMessage();
                message.Type = Messaging.MessageType.ERROR;
                message.Value = e.Message;
            }
            Broadcast(message);
        }

        public void Broadcast(ADMMessage message)
        {
            if (_listener != null && message != null && message.CanBroadcast)
            {
                switch (message.Type)
                {
                    case Chetch.Messaging.MessageType.ERROR:
                        _listener(message, this);
                        break;

                    default:
                        if(State == ADMState.DEVICE_READY || State == ADMState.DEVICE_CONNECTED)
                        {
                            _listener(message, this);
                        }
                        break;
                }
            }
        }

        public void SendCommand(byte targetID, ArduinoCommand command, List<Object> extraArgs = null)
        {
            var message = new ADMMessage();
            message.LittleEndian = LittleEndian;
            message.Type = Messaging.MessageType.COMMAND;
            message.Tag = 0; //TODO: create some kind of perhaps counter-based tagging
            message.TargetID = targetID;
            message.CommandID = (byte)command.Type;
            
            List<Object> allArgs = command.Arguments;
            if(extraArgs != null && extraArgs.Count > 0)
            {
                allArgs.AddRange(extraArgs);
            }

            foreach (Object arg in allArgs)
            {
                byte[] b;
                if (arg is String)
                {
                    b = Chetch.Utilities.Convert.ToBytes((String)arg);
                }
                else if (arg.GetType().IsValueType)
                {
                    b = Chetch.Utilities.Convert.ToBytes((ValueType)arg, LittleEndian);
                }
                else
                {
                    throw new Exception("Unable to process type " + arg.GetType());
                }
                message.AddArgument(b);
            }

            SendMessage(message);
        }

        private void _sleep(int sleep)
        {
            if (sleep > 0)
            {
                Thread.Sleep(sleep);
            }
        }

        public void SendMessage(ADMMessage message, int sleep = 0)
        {
            if(message != null)
            {
                SendString(message.Serialize());
                _sleep(sleep);
            }
        }

        public void SendString(String s)
        {
            if (s == null || s == String.Empty) return;

            if(s.Length > MAX_SEND_STRING_LENGTH)
            {
                String msg = String.Format("Cannot send string {0} as it has length {1} > {2}", s, s.Length, MAX_SEND_STRING_LENGTH);
                Tracing?.TraceEvent(TraceEventType.Error, 0, msg);
#if DEBUG
                Debug.Print(msg);
#endif
                return;
            }

            //System.Diagnostics.Debug.Print("Sending: " + s);
            lock (LockSendString)
            {
                _session.SendStringData(s);
            }
        }

        public void SetDigitalPin(int pinNumber, bool value, int sleep = 0)
        {
            lock(LockSetDigitalPin)
            {
                _session.SetDigitalPin(pinNumber, value);
            }
            _sleep(sleep);
        }


        public void SetDigitalPinMode(int pinNumber, PinMode pinMode, int sleep = 0)
        {
            lock (LockSetDigitalPinMode)
            {
                _session.SetDigitalPinMode(pinNumber, pinMode);
            }
            _sleep(sleep);
        }

        public void SetDigitalReportMode(int portNumber, Boolean enable, int sleep = 0)
        {
            _session.SetDigitalReportMode(portNumber, enable);
            _sleep(sleep);
        }

        public void IssueCommand(String deviceID, String command, List<Object> args = null)
        {
            IssueCommand(deviceID, command, 1, 0, args);
        }

        public void IssueCommand(String deviceID, String command, int repeat, int delay, params Object[] args)
        {
            IssueCommand(deviceID, command, repeat, delay, new List<Object>(args));
        }

        public void IssueCommand(String deviceID, String command, params Object[] args)
        {
            IssueCommand(deviceID, command, 1, 0, new List<Object>(args));
        }

        public void IssueCommand(String deviceID, String command, int repeat, int delay, List<Object> args = null)
        {
            var device = GetDevice(deviceID);
            //check has device
            if(device == null)
            {
                throw new Exception("Cannot find device with ID " + deviceID);
            }
            if (!device.IsConnected)
            {
                throw new Exception(String.Format("Device {0} is not connected", device));
            }

            //check has command
            ArduinoCommand acmd = device.GetCommand(command);
            if (acmd == null)
            {
                throw new Exception(String.Format("Device {0} does not have command {1}", deviceID, command));
            }

            //pass an empty array rather than null ... safety measure here just for the ThreadExecution Manager
            if(args == null)
            {
                args = new List<Object>();
            }

            //Use ThreadExecutionManager to allow for multi-threading by device 
            int prevSize = ThreadExecutionManager.MaxQueueSize;
            ThreadExecutionManager.MaxQueueSize = acmd.IsCompound ? 1 : 256;
            ThreadExecutionManager.Execute<List<Object>>(device.ID, repeat, delay, device.ExecuteCommand, command, args);
            ThreadExecutionManager.MaxQueueSize = prevSize;
        }

        public void RequestStatus(byte boardID = 0)
        {
            var message = new ADMMessage();
            message.Type = Messaging.MessageType.STATUS_REQUEST;
            message.TargetID = boardID;
            SendMessage(message);
        }

        public void Ping()
        {
            var message = new ADMMessage();
            message.Type = Messaging.MessageType.PING;
            message.TargetID = 0;
            SendMessage(message);
        }

        public void Blink(int repeat = 1, int delay = 0)
        {
            if (HasDevice(Devices.Diagnostics.LEDBuiltIn.LED_BUILTIN_ID))
            {
                var device = (Devices.Diagnostics.LEDBuiltIn)GetDevice(Devices.Diagnostics.LEDBuiltIn.LED_BUILTIN_ID);
                if (!device.IsConnected)
                {
                    throw new Exception("Device LEDBuiltIn is not connected");
                }
                device.Blink(repeat, delay);
            } else
            {
                throw new Exception("Cannot blink as no LEDBuiltIn device available");
            }
        }
    }
}
