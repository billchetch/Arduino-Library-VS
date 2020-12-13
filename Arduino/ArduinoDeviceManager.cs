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
using Chetch.Arduino.XBee;
using Chetch.Arduino.Exceptions;
using System.Threading.Tasks;
using System.Linq.Expressions;

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

    public enum ErrorCode
    {
        NO_ERROR = 0,
        ERROR_CHECKSUM = 1,
        ERROR_UNRECOGNISED_MESSAGE_TYPE = 2,
        ERROR_BADLY_FORMED_MESSAGE = 3,
        ERROR_UNKNOWN = 4,
        ERROR_ADM_NOT_INITIALISED = 5,
        ERROR_HOST_GENERATED = 6
    }

    public class ADMMessage : Message
    {
        public class MessageTags
        {
            const long TTL = 5 * 1000; //how long in millis a Tag can last for 
            private long[] _usedTags = new long[255];

            public void Release(byte tag)
            {
                if (tag == 0) return;
                _usedTags[tag] = 0;
            }

            public byte CreateTag()
            {
                //start from 1 as we reserve 0 to mean a non-assigned tag
                long nowInMillis = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                for (byte i = 1; i < _usedTags.Length; i++)
                {
                    if (_usedTags[i] == 0 || nowInMillis - _usedTags[i] > TTL)
                    {
                        _usedTags[i] = nowInMillis;
                        return i;
                    }
                }
                throw new Exception("Cannot create tag as all tags are being used");
            }

            public int Available
            {
                get
                {
                    int available = 0;
                    long nowInMillis = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                    for (byte i = 1; i < _usedTags.Length; i++)
                    {
                        if (_usedTags[i] == 0 || nowInMillis - _usedTags[i] > TTL)
                        {
                            available++;
                        }
                    }
                    return available;
                }
            }

            public void Reset()
            {
                for (byte i = 1; i < _usedTags.Length; i++)
                {
                    _usedTags[i] = 0;
                }
            }
        }

        public static byte CreateCommandID(byte ctype, byte idx)
        {
            byte cid = (byte)((int)ctype + (idx << 4));
            return cid;
        }

        public static bool IsCommandType(byte commandID, byte ctype)
        {
            return GetCommandType(commandID) ==  ctype;
        }

        public static byte GetCommandType(byte commandID)
        {
            return (byte)(commandID & 0xF);
        }

        public static byte GetCommandIndex(byte commandID)
        {
            return (byte)(commandID >> 4);
        }

        public byte Tag { get; set; } = 0; //can be used to track messages
        public byte TargetID { get; set; } = 0; //ID number on board to determine what is beig targeted
        public byte CommandID { get; set; } = 0; //Command ID on board ... basically to identify function e.g. Send or Delete ...
        public byte SenderID { get; set; } = 0; //
        public List<byte[]> Arguments { get; } = new List<byte[]>();
        public bool LittleEndian { get; set; } = true;
        public bool CanBroadcast { get; set; } = true;

        public ADMMessage(byte tag)
        {
            DefaultEncoding = MessageEncoding.BYTES_ARRAY;
            if(tag > 0)Tag = tag;
        }

        public ADMMessage() : this(0) { }

        public override void AddBytes(List<byte> bytes)
        {
            //Scheme here is 4 bytes as a 'header' to include: Type, Tag, TargetID, CommandID
            //Followed by a non-limited (although in reality it should be determined by the board)
            //number of arguments, where each argument is preceded by the number of bytes the argument needs
            //and then finally a checksum of all the bytes
            base.AddBytes(bytes); //will add Type

            bytes.Add(Tag);
            bytes.Add(TargetID);
            bytes.Add(CommandID);
            bytes.Add(SenderID);

            foreach(var b in Arguments)
            {
                bytes.Add((byte)b.Length);
                bytes.AddRange(b);
            }

            byte checksum = Utilities.CheckSum.SimpleAddition(bytes.ToArray());
            bytes.Add(checksum);

            if (bytes.Count > 254) throw new Exception("Message cannot exceed 254 bytes");

            //we now add a byte to handle the zero byte case cos the zero byte is interpreted as string termination
            byte zeroByte = (byte)1;
            while(zeroByte <= 255) {
                bool useable = true;
                foreach(var b in bytes)
                {
                    if(b == zeroByte)
                    {
                        useable = false;
                        break;
                    }
                }
                if (useable) break;
                zeroByte++;
            }

            if(zeroByte >= 1)
            {
                bytes.Insert(0, zeroByte); //use the 'mask' as the encoded zero byte
                for(int i = 1; i < bytes.Count; i++)
                {
                    if (bytes[i] == 0) bytes[i] = zeroByte;
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

        public byte[] Argument(int argIdx)
        {
            return Arguments[argIdx];
        }

        public byte ArgumentAsByte(int argIdx)
        {
            return Argument(argIdx)[0];
        }

        public bool ArgumentAsBool(int argIdx)
        {
            return ArgumentAsByte(argIdx) > 0;
        }

        public int ArgumentAsInt(int argIdx)
        {
            return Chetch.Utilities.Convert.ToInt(Argument(argIdx));
        }

        public long ArgumentAsLong(int argIdx)
        {
            return Chetch.Utilities.Convert.ToLong(Argument(argIdx));
        }

        public float ArgumentAsFloat(int argIdx)
        {
            return Chetch.Utilities.Convert.ToFloat(Argument(argIdx));
        }

        public String ArgumentAsString(int argIdx)
        {
            return Chetch.Utilities.Convert.ToString(Argument(argIdx));
        }

        public override void OnDeserialize(string s, MessageEncoding encoding)
        {
            base.OnDeserialize(s, encoding);

            switch (encoding)
            {
                case MessageEncoding.BYTES_ARRAY:
                    byte[] bytes;
                    try
                    {
                        bytes = Chetch.Utilities.Convert.ToBytes(s);
                        if (bytes.Length < 7) throw new Exception("ADMMessage::onDeserialize message  only has " + bytes.Length + " bytes ... must have 7 or more");
                        byte zeroByte = bytes[0];
                        byte checkbyte = bytes[bytes.Length - 1];
                        byte checksum = 0;

                        //replace zerobyte with 0 and calculate checksum
                        for (int i = 1; i < bytes.Length - 1; i++)
                        {
                            if (bytes[i] == zeroByte) bytes[i] = 0;
                            checksum += bytes[i];
                        }

                        if (checksum != checkbyte) throw new Exception("ADMMessage::onDeserialize checksum does not match checkbyte");

                        //add propoerties
                        Type = (Chetch.Messaging.MessageType)bytes[1];
                        Tag = bytes[2];
                        TargetID = bytes[3];
                        CommandID = bytes[4];
                        SenderID = bytes[5];

                        //convert arguments
                        int argumentIndex = 6; // 1 more than Type, Tag, Target, Command, Sender because first byte is zero byte
                        while (argumentIndex < bytes.Length - 1)
                        {
                            int length = bytes[argumentIndex];
                            byte[] arg = new byte[length];
                            for (int i = 0; i < length; i++)
                            {
                                arg[i] = bytes[argumentIndex + i + 1];
                            }
                            AddArgument(arg);
                            argumentIndex += length + 1;
                        }
                    } catch (Exception e)
                    {
                        throw e;
                    }
                    break;
            } //end encoding switch
        }
    }

    
    /*
     * ADM
     * 
     * One instance per board.
     */

    public class ArduinoDeviceManager
    {
        private const int RESPONSE_TIMEOUT = 10000; //Firmata setting
        private const int MAX_SEND_STRING_LENGTH = 31; //maximum string length to send by Firmata (was 32, reduced to 31 cos message now contains checksum byte)
        private const int MESSAGE_RECEIVED_TIMEOUT = 1500; //how long we hold up Sending the current message while waiting for the previous ADMMessage to be received
        private const int SEND_MESSAGE_LOCK_TIMEOUT = 1500; //number of ms we wait to send a message before allowing thread to continue

        public const string ARDUINO_MEGA_2560 = "USB-SERIAL CH340";
        public const string ARDUINO_UNO = "Arduino Uno";
        public const string XBEE_DIGI = "USB Serial&Digi";

        public const string DEFAULT_BOARD_SET = ARDUINO_MEGA_2560 + "," + ARDUINO_UNO;

        static public List<String> GetBoardPorts(String supportedBoards, List<String> allowedPorts = null, List<String> deniedPorts = null)
        {
            if (supportedBoards == null || supportedBoards == String.Empty)
            {
                throw new Exception("ArduinoDeviceManager:Connect no supportedBoards provided");
            }
            
            //all ports that match supported boards
            List<String> boardPorts = SerialPorts.Find(supportedBoards.Trim());
            bool allowAllPorts = (allowedPorts == null || allowedPorts.Count == 0);
            bool denySomePorts = (deniedPorts != null && deniedPorts.Count > 0);
            if (allowAllPorts && !denySomePorts)
            {
                return boardPorts;
            } else 
            {
                List<String> ports2return = new List<String>();
                foreach(var p in boardPorts)
                {
                    if (deniedPorts != null && deniedPorts.Contains(p)) continue;
                    if (allowedPorts != null && allowedPorts.Contains(p)) ports2return.Add(p);
                }
                return ports2return;
            }
        }

        static public ArduinoDeviceManager Connect(String port, SerialBaudRate bps, int timeOut, Action<ADMMessage, ArduinoDeviceManager> listener)
        {
            ISerialConnection connection = new EnhancedSerialConnection(port, bps);
            
            if (connection != null)
            {
                var session = new ArduinoSession(connection, timeOut);
                try
                {
                    var mgr = new ArduinoDeviceManager(session, listener, port);
                    return mgr;
                } catch (Exception e)
                {
                    if (connection.IsOpen) connection.Close();
                    throw e;
                }
            }
            
            return null;
        }
        
        static public ArduinoDeviceManager Connect(String port, SerialBaudRate bps, Action<ADMMessage, ArduinoDeviceManager> listener)
        {
            return Connect(port, bps, RESPONSE_TIMEOUT, listener);
        }

        static public ArduinoDeviceManager Connect(String port, SerialBaudRate bps)
        {
            return Connect(port, bps, RESPONSE_TIMEOUT, null);
        }

        static public ArduinoDeviceManager Connect(String nodeID, String port, SerialBaudRate bps, int timeOut, Action<ADMMessage, ArduinoDeviceManager> listener)
        {
            if (String.IsNullOrEmpty(nodeID)) throw new Exception("ArduinoDeviceManager::Connect ... nodeID cannot be empty or null");
            ISerialConnection connection = new XBeeFirmataSerialConnection(nodeID, port, bps);
            if (connection != null)
            {
                var session = new ArduinoSession(connection, timeOut);
                try
                {
                    var mgr = new ArduinoDeviceManager(session, listener, port, nodeID);
                    mgr.Connection = connection;
                    return mgr;
                }
                catch (Exception e)
                {
                    if (connection.IsOpen) connection.Close();
                    throw e;
                }
            }

            return null;
        }

        static public ArduinoDeviceManager Connect(String nodeID, String port, SerialBaudRate bps, Action<ADMMessage, ArduinoDeviceManager> listener)
        {
            return Connect(nodeID, port, bps, RESPONSE_TIMEOUT, listener);
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

        public ArduinoSession Session { get { return _session;  } }

        public ISerialConnection Connection { get; set; }

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
                bool devicesConnected = State == ADMState.DEVICE_READY || State == ADMState.DEVICE_CONNECTED;
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
        //Thread Locks
        private Object SendMessageLock = new Object();
        private Object MessageStatsLock = new Object();

        public ADMMessage.MessageTags MessageTags { get; } = new ADMMessage.MessageTags();

        public long MessagesSent { get; internal set; } = 0;
        public long MessagesReceived { get; internal set; } = 0;
        public ADMMessage LastMessageSent { get; internal set; }
        public DateTime LastMessageSentOn { get; internal set; }
        public bool MessageReceivedSuccess { get; internal set; } = false;
        public ADMMessage LastMessageReceived { get; internal set; }
        public DateTime LastMessageReceivedOn { get; internal set; }
        public ADMMessage LastErrorMessage { get; internal set;  }
        public ErrorCode LastErrorCode { get; internal set; }
        public DateTime LastErrorOn { get; internal set; }
        public ADMMessage LastPingResponseMessage { get; internal set; }
        public DateTime LastPingResponseOn { get; internal set; }
        public ADMMessage LastStatusResponseMessage { get; internal set; }
        public DateTime LastStatusResponseOn { get; internal set; }
        public DateTime LastDisconnectedOn { get; internal set; }


        public String Port { get; internal set; }
        public String NodeID { get; internal set; } //if using a shared port

        public String PortAndNodeID {  get { return Port + (String.IsNullOrEmpty(NodeID) ? "" : ":" + NodeID);  } }

        private ArduinoSession _session;
        public byte BoardID { get; internal set; } //Should be set in STATUS_RESPONSE message from board ... this matches this ADM instance with a phsyical Board
        public int LEDBIPin { get; internal set; } = -1; //Should be set in STATUS_RESPONSE message from board
        public bool LittleEndian { get; internal set; } = true;
        public int MaxDevices { get; internal set; } = 0; //This will be set in STATUS_RESPONSE messag from board

        private Dictionary<String, ArduinoDevice> _devices;
        private Dictionary<String, byte> _device2boardID;
        private Dictionary<byte, ArduinoDevice> _boardID2device;
        private Firmware _firmware;
        private ProtocolVersion _protocolVersion;
        private BoardCapability _boardCapability;
        private Dictionary<int, List<ArduinoDevice>> _pin2device;
        private Dictionary<int, DigitalPortState> _portStates;
        private Action<ADMMessage, ArduinoDeviceManager> _listener;

        public List<ArduinoDeviceGroup> DeviceGroups { get; internal set; } = new List<ArduinoDeviceGroup>();

        //interval based sampler for providing averages over device values.
        public Sampler Sampler { get; set; } = null; //To be supplied externally

        public ArduinoDeviceManager(ArduinoSession firmata, Action<ADMMessage, ArduinoDeviceManager> listener, String port, String nodeID = null)
        {
            State = ADMState.CONNECTING;

            _devices = new Dictionary<String, ArduinoDevice>();
            _device2boardID = new Dictionary<String, byte>();
            _boardID2device = new Dictionary<byte, ArduinoDevice>();

            _pin2device = new Dictionary<int, List<ArduinoDevice>>();
            _portStates = new Dictionary<int, DigitalPortState>();

            _session = firmata;
            _listener = listener;
            _session.MessageReceived += HandleFirmataMessageReceived;
            _session.ProcessMessageException += HandleFirmataProcessMessageException;

            Port = port;
            NodeID = nodeID;
            _session.ID = PortAndNodeID;

            _firmware = _session.GetFirmware();
#if DEBUG
            Debug.Print(String.Format("{0}: Firmware: {1} version {2}.{3}", PortAndNodeID, _firmware.Name, _firmware.MajorVersion, _firmware.MinorVersion));
#endif
            _protocolVersion = _session.GetProtocolVersion();
#if DEBUG
            Debug.Print(String.Format("{0}: Firmata protocol version {1}.{2}", PortAndNodeID, _protocolVersion.Major, _protocolVersion.Minor));
#endif
            _boardCapability = _session.GetBoardCapability();
            State = ADMState.CONNECTED;

            //if here and no exceptions then the connection should be good
            //so initialise the board
            Initialise();

            //and request board status
            RequestStatus();
        }

        public void Disconnect()
        {
            foreach(ArduinoDevice dev in _devices.Values)
            {
                dev.Disconnect();
            }
            State = ADMState.NOT_CONNECTED;
            LastDisconnectedOn = DateTime.Now;
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
                throw e;
            }
            catch (UnauthorizedAccessException e)
            {
                throw e;
            }
        }

        public void Clear(int attempts = 1, int waitBetweenAttempts = 0)
        {
            lock (SendMessageLock)
            {
                String errMsg = null;
                Exception innerException = null;
                for (int i = 0; i < attempts; i++)
                {
                    errMsg = null;
                    try
                    {
                        Sampler?.Stop();
                        Tracing?.TraceEvent(TraceEventType.Information, 0, "Attempt ({0} of {1}) to clear session @ {2}", i + 1, attempts, PortAndNodeID);
                        _session.Clear();
                        Tracing?.TraceEvent(TraceEventType.Information, 0, "Session @ {0} cleared", PortAndNodeID);
                        Sampler?.Start();
                        break;
                    }
                    catch (Exception e)
                    {
                        errMsg = String.Format("ArduinoDeviceManager::Clear Session @ {0} produced exception {1} {2}", PortAndNodeID, e.GetType(), e.Message);
                        innerException = e;
                        Tracing?.TraceEvent(TraceEventType.Information, 0, errMsg);

                        if (waitBetweenAttempts > 0 && attempts > 1)
                        {
                            System.Threading.Thread.Sleep(waitBetweenAttempts);
                        }
                    }
                }

                if(errMsg != null)
                {
                    throw new ArduinoException(errMsg, innerException);
                } else
                {
                    //Do some resetting
                    MessageReceivedSuccess = true; //to avoid message timeout exceptions upon the first message sent after this clear
                }
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
            return (int)System.Math.Floor((double)pinNumber / 8.0);
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
            if(device.ID == null)
            {
                throw new Exception("Cannot add this device as it does not have a valid ID");
            }
            if (State != ADMState.DEVICE_READY)
            {
                throw new Exception("Cannot add device " + device.ID + " as ADM state must be DEVICE_READY ... currently " + State);
            }
            if (_devices.ContainsKey(device.ID))
            {
                throw new Exception("Cannot add this device because there is already a device with ID " + device.ID);
            }
            if(_devices.Count >= MaxDevices)
            {
                throw new Exception(String.Format("Cannot add this device because there are already a max of {0} devices.", MaxDevices));
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
                            SetDigitalPin(dpin.PinNumber, dpin.InitialValue > 0);
                        }
                        break;
                }
            }

            return device;
        }

        public void AddDeviceGroup(ArduinoDeviceGroup dg)
        {
            if (dg == null) throw new Exception("No device group to add");

            dg.ADM = this;
            foreach(var dev in dg.Devices)
            {
                AddDevice(dev);
            }
            if(!DeviceGroups.Contains(dg))DeviceGroups.Add(dg);
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
            var boardID = message.TargetID;
            return GetDeviceByBoardID(boardID);
        }

        public bool IsDeviceConnected(String deviceID)
        {
            var d = GetDevice(deviceID);
            return (d != null && d.IsConnected);
        }

        private void HandleFirmataProcessMessageException(Object sender, Exception e)
        {
            String msg = String.Format("{0} error processing firmata message {1}: {2} ", BoardID, e.GetType(), e.Message);
            Tracing?.TraceEvent(TraceEventType.Error, 0, msg);
        }


        /// <summary>
        /// Handle messages that harve arrived from the board and successfully processed by Firmata
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void HandleFirmataMessageReceived(Object sender, FirmataMessageEventArgs eventArgs)
        {
            var fmessage = eventArgs.Value;
            ADMMessage message = null;
            try
            {
                switch (fmessage.Type)
                {
                    case Solid.Arduino.Firmata.MessageType.FirmwareResponse:
                        break;

                    case Solid.Arduino.Firmata.MessageType.StringData:
                        StringData sd = (StringData)fmessage.Value;
                        try
                        {
                            String serialized = sd.Text;
                            message = ADMMessage.Deserialize<ADMMessage>(serialized, MessageEncoding.BYTES_ARRAY);
                        }
                        catch (Exception e)
                        {
                            String sbytes = BitConverter.ToString(Chetch.Utilities.Convert.ToBytes(sd.Text));
                            Tracing?.TraceEvent(TraceEventType.Error, 4000, "Deserializing {0} produced exception {1}: {2}", sbytes, e.GetType(), e.Message);
                            throw e;
                        }
                        break;

                    case Solid.Arduino.Firmata.MessageType.PinStateResponse:
                        break;

                    case Solid.Arduino.Firmata.MessageType.DigitalPortState:
                        DigitalPortState portState = (DigitalPortState)fmessage.Value;
                        String s = BitConverter.ToString(BitConverter.GetBytes(portState.Pins));
                        
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
                            int bit2check = (int)System.Math.Pow(2, i);
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
                byte tag = message != null ? message.Tag : (byte)0;
                message = new ADMMessage();
                message.Type = Messaging.MessageType.ERROR;
                message.Value = e.Message;
                message.Tag = tag;
            }

            if(message != null)
            {
                Task.Run(() => { HandleReceivedADMMessage(message); });
            }
        }

        private void HandleReceivedADMMessage(ADMMessage message)
        {
            try
            {
                //do some general checking
                //TODO: check BoardID matches to SenderID
                lock (MessageStatsLock)
                {
                    LastMessageReceived = message;
                    LastMessageReceivedOn = DateTime.Now;
                    MessageReceivedSuccess = message.Tag == LastMessageSent.Tag;
                    MessagesReceived++;
                }
                //Console.WriteLine("<--------- {0}: Received message {1} tag {2} target {3} from sender {4}", PortAndNodeID, message.Type, message.Tag, message.TargetID, message.SenderID);

                switch (message.Type)
                {
                    case Messaging.MessageType.INITIALISE_RESPONSE:
                        message.AddValue("LittleEndian", message.ArgumentAsBool(0));
                        LittleEndian = message.GetBool("LittleEndian");
                        message.AddValue("FreeMemory", message.ArgumentAsInt(1));
                        BoardID = message.SenderID;
                        message.AddValue("MaxDevices", message.ArgumentAsInt(2));
                        MaxDevices = message.GetInt("MaxDevices");
                        message.AddValue("LEDBI", message.ArgumentAsInt(3));
                        LEDBIPin = message.GetInt("LEDBI");
                        break;

                    case Messaging.MessageType.STATUS_RESPONSE:
                        try
                        {
                            if (message.TargetID == 0)
                            {
                                message.AddValue("FreeMemory", message.ArgumentAsInt(0));
                                message.AddValue("BoardType", message.ArgumentAsString(1));
                                message.AddValue("Initialised", message.ArgumentAsBool(2));
                                message.AddValue("DeviceCount", message.ArgumentAsInt(3));
                            }
                            if (State == ADMState.CONNECTED)
                            {
                                State = ADMState.DEVICE_READY;
                            }
                        }
                        catch (Exception e)
                        {
                            Tracing?.TraceEvent(TraceEventType.Error, 4000, "STATUS_RESPONSE error: {0}, {1}", e.GetType(), e.Message);
                            throw e;
                        }

                        //record this
                        LastStatusResponseMessage = message;
                        LastStatusResponseOn = DateTime.Now;
                        break;

                    case Messaging.MessageType.PING_RESPONSE:
                        //record this
                        LastPingResponseMessage = message;
                        LastPingResponseOn = DateTime.Now;
                        break;

                    case Messaging.MessageType.CONFIGURE_RESPONSE:
                        break;

                    case Messaging.MessageType.ERROR:
                        //record last error message
                        if (message.Arguments.Count)
                        {
                            message.AddValue("ErrorCode", (ErrorCode)message.ArgumentAsByte(0));
                        } else
                        {
                            message.AddValue("ErrorCode", ErrorCode.ERROR_HOST_GENERATED);
                        }
                        LastErrorMessage = message;
                        LastErrorOn = DateTime.Now;
                        break;
                }

                if (State == ADMState.DEVICE_READY || State == ADMState.DEVICE_CONNECTED)
                {
                    //direct messages to devices
                    var dev = GetTargetedDevice(message);
                    if (dev != null)
                    {
#if DEBUG
                        Debug.Print(String.Format("Handling message {0} for device {1} ... connected: {2}, memory: {3}", message.Type, dev.ID, dev.IsConnected, message.HasValue("FM") ? message.GetValue("FM") : "N/A"));
#endif
                        try
                        {
                            dev.HandleMessage(message);
                        }
                        catch (Exception e)
                        {
                            Tracing?.TraceEvent(TraceEventType.Error, 4000, "Handling message for device {0} produced exception {1}: {2}", dev.ID, e.GetType(), e.Message);
                            throw e;
                        }

                    }

                    //we do this test after handling message because the message maybe a CONFIGURE_RESPONSE message which will then set the 'connected' status of the device
                    if (message.Type == Messaging.MessageType.CONFIGURE_RESPONSE && DevicesConnected)
                    {
                        State = ADMState.DEVICE_CONNECTED;
                    }
                }
            } catch (Exception e)
            {
                byte tag = message != null ? message.Tag : (byte)0;
                message = new ADMMessage();
                message.Type = Messaging.MessageType.ERROR;
                message.Value = e.Message;
                message.AddValue("ErrorCode", ErrorCode.ERROR_HOST_GENERATED);
                message.Tag = tag;
            }

            Broadcast(message);
        }

        public void Broadcast(ADMMessage message)
        {
            if (message == null) return;

            MessageTags.Release(message.Tag);
            message.Target = null; //clear the target as it may have been used internally but now this is a broadcast so it's not intended for any specific target

            if (_listener != null && message.CanBroadcast)
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

        public byte SendCommand(byte targetID, ArduinoCommand command, List<Object> extraArgs = null, byte tag = 0)
        {
            var message = new ADMMessage();
            message.LittleEndian = LittleEndian;
            message.Type = Messaging.MessageType.COMMAND;
            message.TargetID = targetID;
            message.Tag = tag == 0 ? MessageTags.CreateTag() : tag;
            message.CommandID = command.ID;
            
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

            return SendMessage(message);
        }

        private void _sleep(int sleep)
        {
            if (sleep > 0)
            {
                Thread.Sleep(sleep);
            }
        }

        public byte SendMessage(ADMMessage message)
        {
            if (message == null) return 0;

            if (Monitor.TryEnter(SendMessageLock, SEND_MESSAGE_LOCK_TIMEOUT))
            {
                try
                {
                    bool ready2send;
                    long msSinceLastSent = -1;

                    //loop waiting for a message response
                    do
                    {
                        msSinceLastSent = (DateTime.Now.Ticks - LastMessageSentOn.Ticks) / TimeSpan.TicksPerMillisecond;
                        if (LastMessageSent == null || MessageReceivedSuccess)
                        {
                            ready2send = true;
                        }
                        else
                        {
                            ready2send = msSinceLastSent > MESSAGE_RECEIVED_TIMEOUT;
                            if (!ready2send)
                            {
                                _sleep(10); //crappy idea to reduce load on cpu
                            }
                            else
                            {
                                //so here we have waited long enough for the previous sent message which has failed to arrive
                                //so we proceed with sending anyway
                                Tracing?.TraceEvent(TraceEventType.Warning, 0, "SendMessage {0}: Sending {1} timed out waiting to receive message {2} tag {3}", PortAndNodeID, message.Type, LastMessageSent.Type, LastMessageSent.Tag);
                            }
                        }
                    } while (!ready2send);

                    //store old values before sending (rollback if exception thrown)
                    //This unusual approach is because it appeared like messages were being received before the SendString could exit
                    ADMMessage lastMessageSent = LastMessageSent;
                    DateTime lastMessageSentOn = LastMessageSentOn;
                    try
                    {
                        lock (MessageStatsLock)
                        {
                            MessagesSent++;
                            LastMessageSent = message;
                            LastMessageSentOn = DateTime.Now;
                            MessageReceivedSuccess = false;
                        }
                        if (message.Tag == 0) message.Tag = MessageTags.CreateTag();
                        message.SenderID = BoardID;
                        //Console.WriteLine("-------------> {0}: Sending message {1} tag {2} target {3}." , PortAndNodeID, message.Type, message.Tag, message.TargetID);
                        SendString(message.Serialize());
                    }
                    catch (Exception e)
                    {
                        lock (MessageStatsLock) //rollback
                        {
                            MessagesSent--;
                            LastMessageSent = lastMessageSent;
                            LastMessageSentOn = lastMessageSentOn;
                        }
                        String errMsg = String.Format("{0} SendMessage: sending {1} (Tag={2}) produced exception {3} {4}", PortAndNodeID, message.Type, message.Tag, e.GetType(), e.Message);
                        Tracing?.TraceEvent(TraceEventType.Error, 0, errMsg);
                        throw e;
                    }
                }
                finally
                {
                    Monitor.Exit(SendMessageLock);
                }

            } //end attempt to try get lock
            else
            {
                //we waited too long to get the lock...
                throw new SendFailedException("ArduinoDeviceManager::SendMessage ... waited to long to obtain lock");
            }
            return message.Tag;
        }

        private void SendString(String s)
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

            try
            {
                _session.SendStringData(s);
            } catch (Exception e)
            {
                String msg = String.Format("Error sending string {0}: {1} ", e.GetType(), e.Message);
                Tracing?.TraceEvent(TraceEventType.Error, 0, msg);
                throw e;
            }
        }

        public void SetDigitalPin(int pinNumber, bool value, int sleep = 0)
        {
            try
            {
                _session.SetDigitalPin(pinNumber, value);
                _sleep(sleep);
            }
            catch (Exception e)
            {
                String msg = String.Format("Error setting digital pin {0} to value {1}, {2}: {3} ", pinNumber, value, e.GetType(), e.Message);
                Tracing?.TraceEvent(TraceEventType.Error, 0, msg);
            }    
        }


        public void SetDigitalPinMode(int pinNumber, PinMode pinMode, int sleep = 0)
        {
            try
            {
                _session.SetDigitalPinMode(pinNumber, pinMode);
                _sleep(sleep);
            } catch (Exception e)
            {
                String msg = String.Format("Error setting digital pin {0} to mode {1}, {2}: {3} ", pinNumber, pinMode, e.GetType(), e.Message);
                Tracing?.TraceEvent(TraceEventType.Error, 0, msg);
            }
        }

        public void SetDigitalReportMode(int portNumber, Boolean enable, int sleep = 0)
        {
            try
            {
                //when setting a port to be reported by Firmata on the arduino it's important that there aren't
                //other devices on pins on that port that may result in an inrush of reports and hence cause too
                //much traffic from the board to the host (which can result in buffers filling up and ulitmately a board reset)
                for (int i = 0; i < 8; i++)
                {
                    int pin = GetPinForPort(portNumber, i);
                    List<ArduinoDevice> devs = GetDevicesByPin(pin);
                    if (devs == null) continue;

                    foreach (ArduinoDevice dev in devs)
                    {
                        bool portAvailable = dev is Devices.SwitchSensor;
                        if (!portAvailable)
                        {
                            String msg = String.Format("ArduinoDeviceManager::SetDigitalReportMode Cannot set digital port {0} as the device {1} on pin {2} is on that port", portNumber, dev.ID, pin);
                            throw new Exception(msg);
                        }
                    }
                }

                //remove any record of the port state so we ensure we get a fresh one
                if (_portStates.ContainsKey(portNumber)) _portStates.Remove(portNumber);
                _session.SetDigitalReportMode(portNumber, enable);
                _sleep(sleep);
            }
            catch (Exception e)
            {
                String msg = String.Format("Error setting digital port {0} to report {1}, {2}: {3} ", portNumber, enable, e.GetType(), e.Message);
                Tracing?.TraceEvent(TraceEventType.Error, 0, msg);
            }
        }

        public byte IssueCommand(String deviceID, String command, List<Object> args = null)
        {
            return IssueCommand(deviceID, command, 1, 0, args);
        }

        public byte IssueCommand(String deviceID, String command, int repeat, int delay, params Object[] args)
        {
            return IssueCommand(deviceID, command, repeat, delay, new List<Object>(args));
        }

        public byte IssueCommand(String deviceID, String command, params Object[] args)
        {
            return IssueCommand(deviceID, command, 1, 0, new List<Object>(args));
        }

        public byte IssueCommand(String deviceID, String command, int repeat, int delay, List<Object> args = null)
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

            return device.ThreadExecuteCommand(command, repeat, delay, args);
        }

        public byte Initialise()
        {
            ADMMessage message = new ADMMessage(MessageTags.CreateTag());
            message.TargetID = 0;
            message.Type = Messaging.MessageType.INITIALISE;
            SendMessage(message);
            return message.Tag;
        }

        public byte RequestStatus(byte boardID = 0)
        {
            var message = new ADMMessage(MessageTags.CreateTag());
            message.Type = Messaging.MessageType.STATUS_REQUEST;
            message.TargetID = boardID;
            SendMessage(message);
            return message.Tag;
        }
        
        public void Configure()
        {
            if (_devices.Count == 0)
            {
                State = ADMState.DEVICE_CONNECTED;
            }
            else
            {
                //send configuration/setup data to board
                foreach (ArduinoDevice device in _devices.Values)
                {
                    var message = new ADMMessage();
                    message.LittleEndian = LittleEndian;
                    message.Type = Messaging.MessageType.CONFIGURE;
                    device.AddConfig(message);
                    SendMessage(message);
                }
            }
        }

        public byte Ping()
        {
            var message = new ADMMessage(MessageTags.CreateTag());
            message.Type = Messaging.MessageType.PING;
            message.TargetID = 0;
            SendMessage(message);
            return message.Tag;
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
