﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Timers;
using Chetch.Services;
using Chetch.Messaging;
using Chetch.Utilities;
using Solid.Arduino;

namespace Chetch.Arduino
{
    abstract public class ADMService : TCPMessagingClient
    {
        public class MessageSchema : Chetch.Messaging.MessageSchema
        {
            public const String BOARD_ID = "BoardID";
            public const String DEVICE_ID = "DeviceID";
            public const String DEVICE_NAME = "DeviceName";
            
            public MessageSchema() { }

            public MessageSchema(Message message) : base(message) { }

            public void PrepareForBroadcast(ArduinoDeviceManager adm)
            {
                ADMMessage message = (ADMMessage)Message;

                message.AddValue("BoardID", adm.BoardID);
                ArduinoDevice dev = null;
                if (message.TargetID > 0)
                {
                    dev = adm.GetDeviceByBoardID(message.TargetID);
                }
                else if (message.Sender != null && message.Sender != String.Empty)
                {
                    dev = adm.GetDevice(message.Sender);
                }
                message.AddValue(DEVICE_ID, dev != null ? dev.ID : "");
                message.AddValue(DEVICE_NAME, dev != null ? dev.Name : "");
            }

            public String GetDeviceID()
            {
                return Message.HasValue(DEVICE_ID) ? Message.GetString(DEVICE_ID) : null;
            }

            public String GetBoardID()
            {
                return Message.HasValue(BOARD_ID) ? Message.GetString(BOARD_ID) : null;
            }

            public String GetDeviceName()
            {
                return Message.HasValue(DEVICE_NAME) ? Message.GetString(DEVICE_NAME) : null;
            }

            public void AddDevices(List<ArduinoDevice> devices, bool listPins = true)
            {
                Dictionary<String, String> d = new Dictionary<String, String>();
                foreach (var dev in devices)
                {
                    d[dev.ID] = dev.ToString(listPins);
                }
                Message.AddValue("Devices", d);
            }

            public void AddDeviceCommands(ArduinoDevice device)
            {
                Message.AddValue("DeviceID", device.ID);
                var cms = device.GetCommands();
                Message.AddValue("DeviceCommands", cms.Select(i => i.CommandAlias).ToList());
            }

            public void AddADMS(Dictionary<String, ArduinoDeviceManager> adms)
            {
                if (adms != null && adms.Count > 0)
                {
                    foreach (ArduinoDeviceManager adm in adms.Values)
                    {
                        Dictionary<String, String> vals = new Dictionary<string, string>();
                        vals["BoardID"] = adm.BoardID.ToString();
                        vals["Port"] = adm.Port;
                        vals["NodeID"] = adm.NodeID?.ToString();
                        vals["State"] = adm.State.ToString();
                        vals["MessagesSent"] = System.Convert.ToString(adm.MessagesSent);
                        vals["MessagesReceived"] = System.Convert.ToString(adm.MessagesReceived);
                        vals["LastMessageSent"] = adm.LastMessageSent == null ? "n/a" : adm.LastMessageSent.Type.ToString();
                        vals["LastMessageSentOn"] = adm.LastMessageSent == null ? "n/a" : adm.LastMessageSentOn.ToString("yyyy-MM-dd HH:mm:ss");
                        vals["LastMessageReceived"] = adm.LastMessageReceived == null ? "n/a" : adm.LastMessageReceived.Type.ToString();
                        vals["LastMessageReceivedOn"] = adm.LastMessageReceived == null ? "n/a" : adm.LastMessageReceivedOn.ToString("yyyy-MM-dd HH:mm:ss");
                        //vals["LastError"] = adm.LastErrorMessage == null ? "n/a" : adm.LastErrorMessage.Value;
                        vals["LastErrorOn"] = adm.LastErrorMessage == null ? "n/a" : adm.LastErrorOn.ToString("yyyy-MM-dd HH:mm:ss");
                        vals["LastStatusResponseOn"] = adm.LastStatusResponseMessage == null ? "n/a" : adm.LastStatusResponseOn.ToString("yyyy-MM-dd HH:mm:ss");
                        vals["LastPingResponseOn"] = adm.LastPingResponseMessage == null ? "n/a" : adm.LastPingResponseOn.ToString("yyyy-MM-dd HH:mm:ss");
                        vals["LastDisconnectedOn"] = adm.LastDisconnectedOn == default(DateTime) ? "n/a" : adm.LastDisconnectedOn.ToString("yyyy-MM-dd HH:mm:ss");
                        vals["AvailableMessageTags"] = System.Convert.ToString(adm.MessageTags.Available);

                        Message.AddValue("Board:" + adm.BoardID, vals);
                    }
                }
                else
                {
                    Message.AddValue("ADMS", "No boards connected");
                }
            }

            public void AddPorts(List<String> ports)
            {
                Message.AddValue("Ports", ports);
            }

            public void AddRequiredBoards(String requiredBoards)
            {
                Message.AddValue("RequiredBoards", (requiredBoards == null || requiredBoards == String.Empty) ? "[any]" : requiredBoards);
            }

            
        } //end message schema class

        public enum ADMEvent
        {
            CONNECTED,
            DISCONNECTED,
            DEVICES_CONNECTED,
            MESSAGE
        }

        public class ADMRequest
        {
            public ArduinoDeviceManager ADM;
            public byte Tag;
            public String Target;
            public long Requested;
            private int _ttl;

            public ADMRequest(ArduinoDeviceManager adm, byte tag, String target, int ttl = 60 * 1000)
            {
                ADM = adm;
                Tag = tag;
                Target = target;
                Requested = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                _ttl = ttl;
            }

            public bool HasExpired()
            {
                long nowInMillis = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                return nowInMillis - Requested > _ttl;
            }
        }

        public class ArduinoDeviceMessageFilter : MessageFilter
        {
            public String ClientName { get { return Sender; } } //change name to better fit with subscription ideas
            public String DeviceID { get; internal set; }

            public ArduinoDeviceMessageFilter(String deviceID, String clientName, MessageType messageType) : base(clientName, messageType, MessageSchema.DEVICE_ID, deviceID)
            {
                DeviceID = deviceID;
            }
            
        }

        //some constants
        protected const int ADM_INACTIVITY_TIMEOUT = 10000; 

        //map of port names to arduino device managers
        protected Dictionary<String, ArduinoDeviceManager> ADMS { get; } = new Dictionary<String, ArduinoDeviceManager>();
        protected String SupportedBoards { get; set; }
        protected String RequiredBoards { get; set; }
        protected int RequiredBoardsCount { //if RequiredBoards is not set then we assume only one board is required
            get
            {
                if (RequiredBoards == null || RequiredBoards == String.Empty) return 1;
                String[] ar = RequiredBoards.Split(',');
                return ar.Length;
            }
        }
        protected bool PortSharing = false;
        
        protected List<String> AllowedPorts { get; } = new List<String>();
        protected List<String> DeniedPorts { get; } = new List<String>();
        protected SerialBaudRate BaudRate { get; set; } = SerialBaudRate.Bps_38400; //SerialBaudRate.Bps_57600;
        protected int ADMInactivityTimeout { get; set; } = ADM_INACTIVITY_TIMEOUT; //in ms
        protected bool AutoStartADMTimer { get; set; }  = true;

        protected Timer _admtimer;
        private Dictionary<String, bool> _devicesConnected = new Dictionary<string, bool>();
        private bool _noPortsFoundWarning = false; //has a no ports found warning been 'traced' ... a flag to prevent multiple trace/log entries
        private Object _lockMonitorADM = new Object(); //lock so we don't disconnect/connect concurrently
        private List<ADMRequest> _admRequests = new List<ADMRequest>();

        //Sampler
        protected Sampler Sampler { get; set; } = new Sampler();

        abstract protected void AddADMDevices(ArduinoDeviceManager adm, ADMMessage message);
        
        public ADMService(String clientName, String clientManagerSource, String serviceSource, String eventLog) : base(clientName, clientManagerSource, serviceSource, eventLog)
        {
            //empty
        }

        protected void AddAllowedPorts(String allowedPorts)
        {
            if (allowedPorts == null || allowedPorts == String.Empty) return;
            List<String> p2a = SerialPorts.ExpandComPortRanges(allowedPorts);
            AllowedPorts.AddRange(p2a);
        }

        protected void AddDeniedPorts(String deniedPorts)
        {
            if (deniedPorts == null || deniedPorts == String.Empty) return;
            List<String> p2a = SerialPorts.ExpandComPortRanges(deniedPorts);
            DeniedPorts.AddRange(p2a);
        }

        protected bool IsRequiredBoard(byte boardID)
        {
            if (RequiredBoards == null || RequiredBoards == String.Empty) return true;
            String[] ar = RequiredBoards.Split(',');
            for (int i = 0; i < ar.Length; i++)
            {
                if (ar[i] == boardID.ToString()) return true;
            }
            return false;
        }

        public ArduinoDeviceManager GetADM(String searchOn)
        {
            if(ADMS.Count == 0)
            {
                return null;
            }

            if (searchOn == null || searchOn.Equals("ADM", StringComparison.OrdinalIgnoreCase) && ADMS.Count == 1)
            {
                return ADMS.First().Value;
            }

            foreach(ArduinoDeviceManager adm in ADMS.Values)
            {
                if (adm == null) continue;

                if (adm.BoardID != 0 && adm.BoardID.ToString().Equals(searchOn, StringComparison.OrdinalIgnoreCase))
                {
                    return adm;
                }
                if(adm.NodeID != null && adm.NodeID.Equals(searchOn, StringComparison.OrdinalIgnoreCase))
                {
                    return adm;
                }
            }

            return null;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                //make config checks here
                if (SupportedBoards == null)
                {
                    throw new Exception("Cannot run service if no supported boards specified.");
                }


                //fire up service with a Chetch Messaging Client available
                base.OnStart(args);

                //fire up the ADM service
                Tracing?.TraceEvent(TraceEventType.Information, 100, 
                    "ADM: Starting ADM service. Supported boards {0}, required boards {1}, allowed ports {2}, denied ports {3}", 
                    SupportedBoards, 
                    RequiredBoards == null ? "any" : RequiredBoards, 
                    AllowedPorts == null || AllowedPorts.Count == 0 ? "all" : String.Join(",", AllowedPorts),
                    DeniedPorts == null || DeniedPorts.Count == 0 ? "none" : String.Join(",", DeniedPorts));

                //create timer
                _admtimer = new System.Timers.Timer();
                _admtimer.Interval = 5000;
                _admtimer.Elapsed += new System.Timers.ElapsedEventHandler(this.MonitorADM);
                if (AutoStartADMTimer)
                {
                    _admtimer.Start();
                    Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Created ADM monitor timer at intervals of {0} with ADMInactivityTimeout set at {1}", _admtimer.Interval, ADMInactivityTimeout);
                }
            }
            catch (Exception e)
            {
                Tracing?.TraceEvent(TraceEventType.Error, 100, "ADM: {0}", e.Message);
                throw e;
            }
        }

        protected override void OnStop()
        {
            if (_admtimer != null)
            {
                lock (_lockMonitorADM)
                { 
                    _admtimer.Stop();
                    if (ADMS.Count > 0)
                    {
                        List<String> ports = new List<String>();
                        foreach (ArduinoDeviceManager adm in ADMS.Values) 
                        {
                            if (!ports.Contains(adm.Port)) ports.Add(adm.Port);
                        }

                        foreach(String port in ports){
                            DisconnectADM(port);
                        }
                        Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Disconnected all ADMs");
                    }
                }
            }
            base.OnStop();
        }

        public override void HandleClientError(Connection cnn, Exception e)
        {
            //throw new NotImplementedException();
        }

        protected void AddADMRequest(ArduinoDeviceManager adm, byte tag, String replyTo)
        {
            if (tag == 0) throw new ArgumentException("AddADMRequest: Tag cannot be 0");
            //we intentionally allow for the possibility of this being overwritten by a future request
            _admRequests.Add(new ADMRequest(adm, tag, replyTo));
        }

        protected ADMRequest GetADMRequest(ArduinoDeviceManager adm, byte tag, bool remove = true)
        {
            ADMRequest req2return = null;
            foreach (ADMRequest req in _admRequests){
                if(req.ADM == adm && req.Tag == tag)
                {
                    req2return = req;
                    break;
                }
            }

            if (req2return != null) _admRequests.Remove(req2return);

            return req2return;
        }
        
        virtual protected bool HandleADMDeviceCommand(ArduinoDeviceManager adm, String deviceID, String command, List<ValueType> args, Message response)
        {
            if (adm == null) throw new Exception("No ADM provided");
            
            if (!adm.HasDevice(deviceID))
            {
                throw new Exception(String.Format("Device {0} has not been added to ADM", deviceID));
            }

            bool respond = true;
            ArduinoDevice device = null;
            MessageSchema schema = new ADMService.MessageSchema(response);
            switch (command)
            {
                case "list-commands":
                    device = adm.GetDevice(deviceID);
                    schema.AddDeviceCommands(device);
                    break;

                case "status":
                    device = adm.GetDevice(deviceID);
                    if(device.BoardID == 0)
                    {
                        throw new Exception(String.Format("Device {0} does not have a board ID", deviceID));
                    }
                    AddADMRequest(adm, adm.RequestStatus(device.BoardID), response.Target);
                    respond = false;
                    break;

                default:
                    var commands = command.Split(',');
                    foreach (var cmd in commands)
                    {
                        var tcmd = cmd.Trim();
                        if (tcmd.ToLower().IndexOf("wait") == 0)
                        {
                            int delay = tcmd.Length > 4 ? System.Convert.ToInt16(tcmd.Substring(4, tcmd.Length - 4)) : 200;
                            System.Threading.Thread.Sleep(delay);
                        }
                        else
                        {
                            byte tag = adm.IssueCommand(deviceID, tcmd, args);
                            if (tag > 0)
                            {
                                AddADMRequest(adm, tag, response.Target);
                                respond = false;
                            }
                        }
                    }
                    break;
            }
            return respond;
        }

        override public void AddCommandHelp()
        {
            base.AddCommandHelp();

            //general commands related to a service or hardware
            AddCommandHelp("status", "Get status info about this service and the ADMs");
            AddCommandHelp("enable-port", "Enables device on port <port>");
            AddCommandHelp("disable-port", "Disables device on port <port>");
            AddCommandHelp("reset-port", "Disables then enables the device on port <port>");

            //adm specific commands related to a board and device
            AddCommandHelp("adm/<board>:status",  "ADM will request board status and add additional information");
            AddCommandHelp("adm/<board>:ping", "ADM will ping board");
            AddCommandHelp("adm/<board>:list-devices", "List devices added to ADM");
            AddCommandHelp("adm/<board>:list-pins", "List used pins and the devices using them");
            AddCommandHelp("adm/<board>:capability", "List pin capabilities");
            AddCommandHelp("adm/<board>:pingloadtest", "Send a rapid <number> of pings with <delay> between each.");
            AddCommandHelp("adm/<board>:setdigitalpin", "Set the <pin number> to <true/false>");
            AddCommandHelp("adm/<board>:setdigitalport", "State the state of <port> to <value>");
            AddCommandHelp("adm/<board>:<device>:wait", "Pause for a short while, useful if interspersed with other, comma-seperated, commands");
            AddCommandHelp("adm/<board>:<device>:list-commands", "List device commands");
        }

        override public bool HandleCommand(Connection cnn, Message message, String cmd, List<ValueType> args, Message response)
        {
            bool respond = true;
            MessageSchema schema = new ADMService.MessageSchema(response);
            DeviceManager devMgr;
            ArduinoDeviceManager adm;

            switch (cmd)
            {
                case "status":
                    schema.AddADMS(ADMS);
                    List<String> ports = ArduinoDeviceManager.GetBoardPorts(SupportedBoards, AllowedPorts, DeniedPorts);
                    schema.AddPorts(ports);
                    schema.AddRequiredBoards(RequiredBoards);

                    devMgr = DeviceManager.GetInstance();
                    List<String> portDevices = new List<String>();
                    foreach (String port in ports)
                    {
                        List<DeviceManager.DeviceInfo> devs = devMgr.GetDevices("(" + port + ")");
                        foreach (DeviceManager.DeviceInfo devInfo in devs)
                        {
                            String s = String.Format("{0} ({1}): {2}", devInfo.Description, devInfo.InstanceID, devInfo.Status);
                            portDevices.Add(s);
                        }
                    }
                    response.AddValue("PortDevices", portDevices);
                    break;

                case "disable-port":
                case "enable-port":
                case "reset-port":
                    if (args.Count != 1 || args[0] == null || args[0].ToString() == String.Empty) throw new Exception("No port specified");
                    String devicePort = args[0].ToString().ToUpper();
                    devMgr = DeviceManager.GetInstance();
                    List<DeviceManager.DeviceInfo> ar = devMgr.GetDevices("(" + devicePort + ")");
                    if (ar.Count != 1) throw new Exception("Cannot find device on port " + devicePort);
                    DeviceManager.DeviceInfo di = ar[0];

                    //first we check if there is an ADM on this port
                    String output = "";
                    lock (_lockMonitorADM)
                    {
                        if (ADMS.ContainsKey(devicePort))
                        {
                            output += String.Format("Found ADM on port {0} so disconnecting first", devicePort) + Environment.NewLine;
                            DisconnectADM(devicePort);
                            System.Threading.Thread.Sleep(500);
                            if (SerialPorts.IsOpen(devicePort)) throw new Exception("Serial port is open on " + devicePort);
                        }

                        Process proc = null;
                        if (cmd == "disable-port")
                        {
                            proc = devMgr.DisableDevice(di.InstanceID);
                        }
                        else if (cmd == "enable-port")
                        {
                            proc = devMgr.EnableDevice(di.InstanceID);
                        }
                        else
                        {
                            proc = devMgr.ResetDevice(di.InstanceID);
                        }
                        output += proc.StandardOutput.ReadToEnd();
                    }
                    response.Value = output;
                    break;

                default:
                    var tgtcmd = cmd.Split(':');
                    if(tgtcmd.Length < 2)
                    {
                        throw new Exception(String.Format("ADM: Unrecognised command {0}", cmd));
                    }

                    //Check that there are any boards connected
                    if(ADMS.Count == 0)
                    {
                        throw new Exception("ADM: No boards connected");
                    }

                    //so this is an ADM command, find the board first
                    adm = GetADM(tgtcmd[0]);
                    
                    if (adm == null)
                    {
                        throw new Exception(String.Format("ADM: Cannot find ADM {0}", tgtcmd[0]));
                    }

                    if (!adm.IsConnected)
                    {
                        throw new Exception(String.Format("ADM: {0} is not conntected", adm.BoardID));
                    }

                    //handle commands related to the board (i.e. not to a specific added device)
                    if (tgtcmd.Length == 2)
                    {
                        int repeat; //frequently used var name
                        int delay; //frequently used var name
                        switch (tgtcmd[1].ToLower())
                        {
                            case "status":
                                AddADMRequest(adm, adm.RequestStatus(), response.Target);
                                respond = false;
                                break;

                            case "ping":
                                AddADMRequest(adm, adm.Ping(), response.Target);
                                respond = false;
                                break;

                            case "pingloadtest":
                                repeat = args != null && args.Count > 0 ? System.Convert.ToInt16(args[0]) : 10;
                                delay = args != null && args.Count > 1 ? System.Convert.ToInt16(args[1]) : 500;
                                Task.Run(() =>
                                {
                                    for (int i = 0; i < repeat; i++)
                                    {
                                        adm.Ping();
                                        System.Threading.Thread.Sleep(delay);
                                    }
                                });
                                respond = false;
                                break;

                            case "capability":
                                var lbc = adm.ListBoardCapability();
                                response.AddValue("PinCount: ", lbc.Count);
                                response.AddValue("Pins", lbc);
                                break;

                            case "disconnect":
                                DisconnectADM(adm.Port);
                                break;

                            case "setdigitalpin":
                                if(args.Count < 2)
                                {
                                    throw new Exception("Insufficient arguments ... must supply a pin number and value");
                                }
                                int pin = System.Convert.ToInt16(args[0]);
                                bool val = Chetch.Utilities.Convert.ToBoolean(args[1]);
                                if(!adm.IsPinCapable(pin, Solid.Arduino.Firmata.PinMode.DigitalOutput))
                                {
                                    throw new Exception(String.Format("Pin {0} is not capabale of digital output", pin));
                                }
                                if(adm.GetDevicesByPin(pin) != null)
                                {
                                    throw new Exception(String.Format("Pin {0} is being used by a device", pin));
                                }
                                adm.SetDigitalPinMode(pin, Solid.Arduino.Firmata.PinMode.DigitalOutput);
                                adm.SetDigitalPin(pin, val);
                                break;

                            case "setdigitalport":
                                if (args.Count < 2)
                                {
                                    throw new Exception("Insufficient arguments ... must supply a port number and value");
                                }
                                int port = System.Convert.ToInt16(args[0]);
                                bool enable = Chetch.Utilities.Convert.ToBoolean(args[1]);
                                adm.SetDigitalReportMode(port, enable);
                                //adm.G
                                break;

                            case "list-devices":
                                schema.AddDevices(adm.GetDevices());
                                break;

                            case "list-pins":

                                break;

                            default:
                                throw new Exception(String.Format("No ADM direct command {0}", tgtcmd[1]));
                        }
                    }
                    else
                    {
                        //handle command specific to device
                        try
                        {
                            respond = HandleADMDeviceCommand(adm, tgtcmd[1], tgtcmd[2], args, response);
                        } catch (Exception e)
                        {
                            Tracing?.TraceEvent(TraceEventType.Error, 0, "Exception: {0}", e.Message);
                            throw e;
                        }
                    }

                    if (response.Value == null || response.Value == String.Empty)
                    {
                        response.Value = "Handled " + cmd;
                    }
                    
                    break;
            }

            return respond;
        }

        virtual protected void ConnectADM(String port, String nodeID = null)
        {
            //turn off the sampler ... it will start again when all boards have been connected
            Sampler.Stop();

            if (PortSharing)
            {
                if (RequiredBoards == null) throw new Exception("If using a shared port, 'Nodes' on the port must be specified as the 'RequiredBoards' property");

                //when using a shared port we connect all the boards in one go
                var boards = nodeID == null ? RequiredBoards.Split(',') : new String[] { nodeID };
                List<Exception> exs = new List<Exception>();

                foreach (String nid in boards)
                {
                    String key = port + ":" + nid;
                    if (ADMS.ContainsKey(key)) continue ;

                    try
                    {
                        //now proceed to connect
                        ADMS[key] = null; //reserve a place (cos the connection process takes a while)
                        Tracing?.TraceEvent(TraceEventType.Information, 100, "ADMService::ConnectADM: Attempting to connect board @ {0}", key);
                        ArduinoDeviceManager adm = ArduinoDeviceManager.Connect(nid, port, BaudRate, TryHandleADMMessage);
                        adm.Sampler = Sampler;
                        adm.Tracing = Tracing;
                        ADMS[key] = adm;
                        _devicesConnected[key] = false;
                        Tracing?.TraceEvent(TraceEventType.Information, 100, "ADMService::ConnectADM: Connected to board @ {0}", key);
                        Broadcast(ADMEvent.CONNECTED, String.Format("ADM now Connected @ {0}", key));

                        //Wait here (i.e. before connecting to another board) until the adm is of status device connected
                        while (!adm.DevicesConnected)
                        {
                            System.Threading.Thread.Sleep(1000);
                        }
                    } catch (Exception e)
                    {
                        ADMS.Remove(key);

                        //TODO: remove the trace and create a single 'aggregate' exception and throw that after th eloop
                        Tracing?.TraceEvent(TraceEventType.Error, 100, "ADMService::ConnectADM: Error connection to board @ {0}: {1}", key, e.Message);
                        exs.Add(e);
                    }
                } //end loop of all required boards
            } else
            {
                String key = port;
                if (ADMS.ContainsKey(key)) throw new Exception("ADMService::ConnectADM: port " + port + " already has an assigned ADM");

                Tracing?.TraceEvent(TraceEventType.Information, 100, "ADMService::ConnectADM: Attempting to connect board on {0} port {0}", port);
                try
                {
                    ADMS[key] = null; //reserve a place (cos the connection process takes a while)

                    //now connct
                    ArduinoDeviceManager adm = ArduinoDeviceManager.Connect(port, BaudRate, TryHandleADMMessage);
                    adm.Sampler = Sampler;
                    adm.Tracing = Tracing;
                    ADMS[key] = adm;
                    _devicesConnected[key] = false;
                    Tracing?.TraceEvent(TraceEventType.Information, 100, "ADMService::ConnectADM: Connected to board on port {0}", port);
                    Broadcast(ADMEvent.CONNECTED, String.Format("Connected ADM to port {0}", port));
                } catch (Exception e){
                    ADMS.Remove(key);

                    //TODO: remove the trace and create a single 'aggregate' exception and throw that after th eloop
                    Tracing?.TraceEvent(TraceEventType.Error, 100, "ADMService::ConnectADM: Error connection to board @ {0}: {1}", key, e.Message);
                }
            }
        }

        virtual protected void DisconnectADM(String port, String nodeID = null)
        {
            if (PortSharing)
            {
                //when using a shared port we connect all the boards in one go
                var ar = nodeID == null ? RequiredBoards.Split(',') : new String[] { nodeID };
                foreach (String nid in ar)
                {
                    String key = port + ":" + nid;
                    if (!ADMS.ContainsKey(key)) continue;

                    Tracing?.TraceEvent(TraceEventType.Information, 100, "ADMService::DisconnectADM: Attempting to disconnect board @ {0}", key);
                    ArduinoDeviceManager adm = ADMS[key];
                    if (adm != null)
                    {
                        adm.Disconnect();
                    }
                    ADMS.Remove(key);
                    _devicesConnected.Remove(key);
                    Tracing?.TraceEvent(TraceEventType.Information, 100, "ADMService::DisconnectADM: Disconnected board @ {0}", key);
                }
            }
            else
            {
                if (!ADMS.ContainsKey(port)) return;

                ArduinoDeviceManager adm = ADMS[port];
                if (adm != null)
                {
                    adm.Disconnect();
                }
                ADMS.Remove(port);
                _devicesConnected.Remove(port);

                Tracing?.TraceEvent(TraceEventType.Information, 100, "ADMService::DisconnectADM: Board {0} on port {1} disconnected", adm.BoardID, port);
                Broadcast(ADMEvent.DISCONNECTED, String.Format("{0} disconnected from port {1}", adm.BoardID, port));
            }
        }

        virtual protected void ReconnectADM(ArduinoDeviceManager adm)
        {
            DisconnectADM(adm.Port, adm.NodeID);
            System.Threading.Thread.Sleep(1000);
            ConnectADM(adm.Port, adm.NodeID);
        }

        virtual protected void ResetPort(String port, Exception e)
        {
            //ensure that if an ADM is connected then we disconnect it first
            DisconnectADM(port);
            System.Threading.Thread.Sleep(500);
            if (SerialPorts.IsOpen(port)) throw new Exception("Serial port is open on " + port);
            
            //now we disable-enable the device connected to the port
            DeviceManager devMgr = DeviceManager.GetInstance();
            List<DeviceManager.DeviceInfo> ar = devMgr.GetDevices("(" + port + ")");
            if (ar.Count == 1)
            {
                DeviceManager.DeviceInfo di = ar[0];
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Attempting reset of device {0} of status {1} on port {2}", di.Description, di.Status, port); ;
                Process proc = devMgr.ResetDevice(di.InstanceID);
                String output = proc.StandardOutput.ReadToEnd();
                Tracing?.TraceEvent(TraceEventType.Information, 0, output);
            } else
            {
                Tracing?.TraceEvent(TraceEventType.Warning, 0, "ADMService::ResetPort Could not find a unique device for port {0}", port);
            }
        }

        protected virtual bool OnADMInactivityTimeout(ArduinoDeviceManager adm, long msQuiet)
        {
            Tracing?.TraceEvent(TraceEventType.Warning, 100, "ADMService::MonitorADM: Last activity of ADM (BoardID={0}) on {1} was {2} ms ago ... so attempting a clear", adm.BoardID, adm.PortAndNodeID, msQuiet);
            try
            {
                adm.Clear();
                Tracing?.TraceEvent(TraceEventType.Information, 100, "ADMService::MonitorADM: Clearing ADM (BoardID={0}) on {1} was successful so attempting to Ping", adm.BoardID, adm.PortAndNodeID);
                adm.RequestStatus();
                return true;
            }
            catch (Exception e)
            {
                Tracing?.TraceEvent(TraceEventType.Error, 100, "ADMService::MonitorADM: Error clearing ADM (BoardID={0}) on {1}: {2} {3}", adm.BoardID, adm.PortAndNodeID, e.GetType(), e.Message);
                return false;
            }
        }

        /// <summary>
        /// Called by a timer
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        protected virtual void MonitorADM(Object sender, ElapsedEventArgs eventArgs)
        {
            _admtimer.Stop();

            try
            {
                lock (_lockMonitorADM)
                {
                    //get all current ports that have boards connected
                    List<String> ports = ArduinoDeviceManager.GetBoardPorts(SupportedBoards, AllowedPorts, DeniedPorts);

                    //build a list of any ADMs that are no longer connected to one of these ports (e.g. USB has been yanked out)
                    List<ArduinoDeviceManager> adms = ADMS.Values.ToList();
                    foreach (ArduinoDeviceManager adm in adms)
                    {
                        if (adm == null) continue;

                        if (!ports.Contains(adm.Port))
                        {
                            //if for some reason (config change?) the port for the ADM is not a possible board port
                            DisconnectADM(adm.Port);
                        }
                    }
                    
                    //now we try and connect all the boards that we have not just disconnected but have not yet been connected
                    if (ports.Count > 0 && ADMS.Count < RequiredBoardsCount)
                    {
                        _noPortsFoundWarning = false;
                        foreach (String port in ports)
                        {
                            try
                            {
                                ConnectADM(port);
                            } 
                            catch (System.IO.IOException e)
                            {
                                switch (e.HResult)
                                {
                                    case Win32.ERROR_ATTACHED_DEVICE_NOT_FUNCTIONING:
                                        ResetPort(port, e);
                                        break;
                                }
                                Tracing?.TraceEvent(TraceEventType.Error, 100, "ADMService::MonitorADM: Connecting ADM to port {0} produced IOException {1} ({2}): {3}", port, e.GetType().ToString(), e.HResult, e.Message);
                            }
                            catch (Exception e)
                            {
                                Tracing?.TraceEvent(TraceEventType.Error, 100, "ADMService::MonitorADM: Connecting ADM to port {0} produced Exception {1} ({2}): {3}", port, e.GetType().ToString(), e.HResult, e.Message);
                            }
                        }
                    }
                    else if(ADMS.Count < RequiredBoardsCount)
                    {
                        if (!_noPortsFoundWarning)
                        {
                            Tracing?.TraceEvent(TraceEventType.Warning, 100, "ADMService::MonitorADM: No boards connected to any port");
                            _noPortsFoundWarning = true;
                        }
                    }


                    //Finally we check on the state of the ADM
                    foreach (ArduinoDeviceManager adm in adms)
                    {
                        if (adm == null) continue;

                        if (!adm.IsConnected)
                        {
                            //TODO: how do we handle this?
                            Tracing?.TraceEvent(TraceEventType.Warning, 100, "ADM (BDID={0}) @ {1} is of state", adm.State);
                        }
                        else if(ADMInactivityTimeout > 0)
                        {
                            //if there has been a suspicioius lack of activity...
                            long msQuiet = (DateTime.Now.Ticks - adm.LastMessageReceivedOn.Ticks) / TimeSpan.TicksPerMillisecond;
                            if (msQuiet > ADMInactivityTimeout)
                            {
                                OnADMInactivityTimeout(adm, msQuiet);
                            }
                        }
                    }

                } //end of monitor lock
            } catch (Exception e)
            {
                Tracing?.TraceEvent(TraceEventType.Error, 100, "Unknown exception in MonitorADM: {0}, {1}", e.GetType().ToString(), e.Message);
            }

            _admtimer.Start();
        }

        //messaging
        protected Message CreateNotification(ADMEvent admEvent, String msg = null)
        {
            var message = new Message(MessageType.NOTIFICATION);
            message.SubType = (int)Server.NotificationEvent.CUSTOM;
            message.AddValue("ADMEvent", admEvent);
            message.Value = msg;

            return message;
        }

        protected void Broadcast(ADMEvent admEvent, String msg = null)
        {
            Message message = CreateNotification(admEvent, msg);
            Broadcast(admEvent, message);
        }

        virtual protected void Broadcast(ADMEvent admEvent, Message message)
        {
            Broadcast(message);
        }

        private void TryHandleADMMessage(ADMMessage message, ArduinoDeviceManager adm)
        {
            try
            {
                HandleADMMessage(message, adm);
            } catch (Exception e)
            {
                Tracing?.TraceEvent(TraceEventType.Error, 100, "HandleADMMessage Error handling {0}: {1}", message.Type, e.Message);
            }
        }

        virtual protected void HandleADMMessage(ADMMessage message, ArduinoDeviceManager adm)
        {
            switch (message.Type)
            {
                case MessageType.ERROR:
                    ErrorCode errCode = message.HasValue("ErrorCode") ? message.GetEnum<ErrorCode>("ErrorCode") : ErrorCode.ERROR_UNKNOWN;
                    Tracing?.TraceEvent(TraceEventType.Error, 100, "ADM {0} produced error: {1}", adm.BoardID, errCode);
                    switch (errCode)
                    {
                        case ErrorCode.ERROR_ADM_NOT_INITIALISED:
                            ReconnectADM(adm);
                            break;

                        default:
                            //currently do nothing
                            break;
                    }
                    break;

                case MessageType.WARNING:
                    Tracing?.TraceEvent(TraceEventType.Warning, 100, "ADM {0} produced warning: {1}", adm.BoardID, message.Value);
                    break;

                case MessageType.STATUS_RESPONSE:
                    if(!PortSharing && !IsRequiredBoard(adm.BoardID))
                    {
                        Tracing?.TraceEvent(TraceEventType.Warning, 100, "ADM {0} is not one of the required boards ({1}).  Disconnecting from port {2}...", adm.BoardID, RequiredBoards, adm.Port);
                        DisconnectADM(adm.Port);
                        break;
                    }

                    if (adm.State == ADMState.DEVICE_READY)
                    {
                        Tracing?.TraceEvent(TraceEventType.Verbose, 100, "ADM: Ready to add devices to {0} on port {1} ...", adm.BoardID, adm.PortAndNodeID);
                        AddADMDevices(adm, message);
                        Tracing?.TraceEvent(TraceEventType.Verbose, 100, "ADM: {0} devices added to {1} on port {2}. Now configure board", adm.DeviceCount, adm.BoardID, adm.PortAndNodeID);
                        adm.Configure();
                    } else if(adm.State == ADMState.DEVICE_CONNECTED)
                    {
                        if(message.HasValue("Initialised") && !message.GetBool("Initialised"))
                        {
                            //Console.WriteLine("Whoa ... {0} has reset without us knowing", adm.PortAndNodeID);
                        }
                    }
                    break;

                case MessageType.CONFIGURE_RESPONSE:
                    String key = adm.PortAndNodeID;
                    if (adm.State == ADMState.DEVICE_CONNECTED && !_devicesConnected[key])
                    {
                        Tracing?.TraceEvent(TraceEventType.Verbose, 100, "ADM: All {0} devices now configured and connected to board {1} on port {2}", adm.DeviceCount, adm.BoardID, adm.PortAndNodeID);
                        OnADMDevicesConnected(adm, message);
                    }
                    break;

                case MessageType.PING_RESPONSE:
                    break;

            }
            
            if(message.Tag > 0)
            {
                ADMRequest req = GetADMRequest(adm, message.Tag);
                if (req != null)
                {
                    if (req.HasExpired())
                    {
                        Tracing?.TraceEvent(TraceEventType.Warning, 0, "ADM request for tag {0} and target {1} has expired so not returning message of type {2}", message.Tag, req.Target, message.Type);
                        return;
                    }
                    else
                    {
                        message.Target = req.Target;
                    }
                }
            }

            var schema = new ADMService.MessageSchema(message);
            schema.PrepareForBroadcast(adm);

            //notify other clients listening to this client
            Broadcast(ADMEvent.MESSAGE, message);
        }

        virtual protected void OnADMDevicesConnected(ArduinoDeviceManager adm, ADMMessage message)
        {
            _devicesConnected[adm.PortAndNodeID] = true;
            bool startSampler = false;
            if (String.IsNullOrEmpty(RequiredBoards))
            {
                startSampler = true;
            }
            else
            {
                String[] ar = RequiredBoards.Split(',');
                startSampler= true;
                for (int i = 0; i < ar.Length; i++)
                {
                    ArduinoDeviceManager amg = GetADM(ar[i]);
                    if (amg == null || amg.State != ADMState.DEVICE_CONNECTED) 
                    {
                        startSampler = false;
                        break;
                    }
                }
            }
            if (startSampler)
            {
                Sampler.Start();
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Started sampler with {0} subjects and tick time of {1}", Sampler.SubjectCount, Sampler.TimerInterval);
            }

            String msg = String.Format("All {0} added devices connected for {1} ", adm.DeviceCount, adm.BoardID);
            Broadcast(ADMEvent.DEVICES_CONNECTED, msg);
        }
    } //end class
}
