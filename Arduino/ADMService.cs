using System;
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
                        vals["BoardID"] = adm.BoardID;
                        vals["Port"] = adm.Port;
                        vals["LastError"] = adm.LastErrorMessage == null ? "n/a" : adm.LastErrorMessage.Value;
                        vals["LastErrorOn"] = adm.LastErrorMessage == null ? "n/a" : adm.LastErrorOn.ToString("yyyy-MM-dd HH:mm:ss");
                        vals["LastStatusResponseOn"] = adm.LastStatusResponseMessage == null ? "n/a" : adm.LastStatusResponseOn.ToString("yyyy-MM-dd HH:mm:ss");
                        vals["LastPingResponseOn"] = adm.LastPingResponseMessage == null ? "n/a" : adm.LastPingResponseOn.ToString("yyyy-MM-dd HH:mm:ss");
                        vals["LastDisconnectedOn"] = adm.LastDisconnectedOn == default(DateTime) ? "n/a" : adm.LastDisconnectedOn.ToString("yyyy-MM-dd HH:mm:ss");
                        vals["AvailableMessageTags"] = System.Convert.ToString(ADMMessage.AvailableTags());

                        Message.AddValue(adm.BoardID, vals);
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
        
        protected List<String> AllowedPorts { get; } = new List<String>();
        protected List<String> DeniedPorts { get; } = new List<String>();
        protected SerialBaudRate BaudRate { get; set; } = SerialBaudRate.Bps_57600;
        protected int MaxPingResponseTime { get; set; } = 20; //in seconds

        protected Timer _admtimer;
        private Dictionary<String, bool> _devicesConnected = new Dictionary<string, bool>();
        private bool _noPortsFoundWarning = false; //has a no ports found warning been 'traced' ... a flag to prevent multiple trace/log entries
        private Object _lockMonitorADM = new Object(); //lock so we don't disconnect/connect concurrently
        private List<ADMRequest> _admRequests = new List<ADMRequest>();
        

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

        protected bool IsRequiredBoard(String boardID)
        {
            if (RequiredBoards == null || RequiredBoards == String.Empty) return true;
            String[] ar = RequiredBoards.Split(',');
            return ar.Contains(boardID);
        }

        protected ArduinoDeviceManager GetADM(String boardID)
        {
            if(ADMS.Count == 0)
            {
                return null;
            }

            if (boardID == null || boardID.Equals("ADM", StringComparison.OrdinalIgnoreCase) && ADMS.Count == 1)
            {
                return ADMS.First().Value;
            }

            foreach(ArduinoDeviceManager adm in ADMS.Values)
            {
                if (adm != null && adm.BoardID != null && adm.BoardID.Equals(boardID, StringComparison.OrdinalIgnoreCase)) return adm;
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
                //_admtimer.Start();
                Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Created ADM monitor timer at intervals of {0}", _admtimer.Interval);
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
                        foreach(ArduinoDeviceManager adm in ADMS.Values){
                            Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Disconnecting ADM for board {0}", adm.BoardID);
                            adm.Disconnect();
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
        
        virtual protected bool HandleADMDeviceCommand(ArduinoDeviceManager adm, String deviceID, String command, List<Object> args, Message response)
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

            //general commands related to a service
            AddCommandHelp("status", "Get status info about this service and the ADMs");
            AddCommandHelp("enable-device", "Enables device on port <port>");
            AddCommandHelp("disable-device", "Disables device on port <port>");
            AddCommandHelp("reset-device", "Disables then enables the device on port <port>");

            //adm specific commands related to a board and device
            AddCommandHelp("adm/<board>:status",  "ADM will request board status and add additional information");
            AddCommandHelp("adm/<board>:list-devices", "List devices added to ADM");
            AddCommandHelp("adm/<board>:list-pins", "List used pins and the devices using them");
            AddCommandHelp("adm/<board>:capability", "List pin capabilities");
            AddCommandHelp("adm/<board>:disconnect", "Disconnect ADM .. should reconnect shortly after");
            AddCommandHelp("adm/<board>:pingloadtest", "Send a rapid <number> of pings with <delay> between each.");
            AddCommandHelp("adm/<board>:setdigitalpin", "Set the <pin number> to <true/false>");
            AddCommandHelp("adm/<board>:<device>:wait", "Pause for a short while, useful if interspersed with other, comma-seperated, commands");
            AddCommandHelp("adm/<board>:<device>:list-commands", "List device commands");
        }

        override public bool HandleCommand(Connection cnn, Message message, String cmd, List<Object> args, Message response)
        {
            bool respond = true;
            MessageSchema schema = new ADMService.MessageSchema(response);
            DeviceManager devMgr;
            
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

                case "disable-device":
                case "enable-device":
                case "reset-device":
                    if (args.Count != 1 || args[0] == null || args[0] == String.Empty) throw new Exception("No port specified");
                    String devicePort = args[0].ToString();
                    devMgr = DeviceManager.GetInstance();
                    List<DeviceManager.DeviceInfo> ar = devMgr.GetDevices("(" + devicePort + ")");
                    if (ar.Count != 1) throw new Exception("Cannot find device on port " + devicePort);
                    DeviceManager.DeviceInfo di = ar[0];
                    Process proc = null;
                    String output = "";
                    if (cmd == "disable-device")
                    {
                        proc = devMgr.DisableDevice(di.InstanceID);
                    } else if(cmd == "enable-device")
                    {
                        proc = devMgr.EnableDevice(di.InstanceID);
                        output = proc.StandardOutput.ReadToEnd();
                    }
                    else
                    {
                        proc = devMgr.DisableDevice(di.InstanceID);
                        output = proc.StandardOutput.ReadToEnd();
                        System.Threading.Thread.Sleep(500);
                        proc = devMgr.EnableDevice(di.InstanceID);
                    }
                    output += proc.StandardOutput.ReadToEnd();
                    response.AddValue("Output", output);
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
                    ArduinoDeviceManager adm = GetADM(tgtcmd[0]);
                    
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

                            case "pingloadtest":
                                repeat = args != null && args.Count > 0 ? System.Convert.ToInt16(args[0]) : 10;
                                delay = args != null && args.Count > 1 ? System.Convert.ToInt16(args[1]) : 500;
                                for (int i = 0; i < repeat; i++)
                                {
                                    adm.Ping();
                                    System.Threading.Thread.Sleep(delay);
                                }
                                respond = false;
                                break;

                            case "capability":
                                var lbc = adm.ListBoardCapability();
                                response.AddValue("PinCount: ", lbc.Count);
                                response.AddValue("Pins", lbc);
                                break;

                            case "disconnect":
                                adm.Disconnect();
                                Broadcast(ADMEvent.DISCONNECTED, String.Format("{0} disconnected from port {1}", adm.BoardID, adm.Port));
                                break;

                            case "setdigitalpin":
                                if(args.Count < 2)
                                {
                                    throw new Exception("Insufficient arguments ... must supply a pin number and value");
                                }
                                int pin =System.Convert.ToInt16(args[0]);
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

        protected void ConnectADM(String port)
        {
            Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Attempting to connect board on port {0}", port);
            ArduinoDeviceManager adm = ArduinoDeviceManager.Connect(port, BaudRate, TryHandleADMMessage);
            adm.Tracing = Tracing;
            ADMS[port] = adm;
            _devicesConnected[port] = false;
            Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Connected to board on port {0}", port);
            Broadcast(ADMEvent.CONNECTED, String.Format("Connected ADM to port {0}", port));
        }

        protected void DisconnectADM(String port)
        {
            try
            {
                ArduinoDeviceManager adm = ADMS[port];
                ADMS.Remove(port);
                _devicesConnected.Remove(port);

                Tracing?.TraceEvent(TraceEventType.Warning, 100, "ADM: Board {0} on port {1} disconnected", adm.BoardID, port);
                Broadcast(ADMEvent.DISCONNECTED, String.Format("{0} disconnected from port {1}", adm.BoardID, port));
            }
            catch (System.IO.IOException e)
            {
                Tracing?.TraceEvent(TraceEventType.Error, 100, "ADM: Connect causes IO exception {0}: {1} on port {2}", e.GetType().ToString(), e.Message, port);
            }
            catch (Exception e)
            {
                Tracing?.TraceEvent(TraceEventType.Error, 100, "ADM: Failed to connect, exception {0}: {1}", e.GetType().ToString(), e.Message);
            }
        }



        /// <summary>
        /// Called by a timer
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        public virtual void MonitorADM(Object sender, ElapsedEventArgs eventArgs)
        {
            _admtimer.Stop();

            try
            {
                lock (_lockMonitorADM)
                {
                    //get all current ports that have boards connected
                    List<String> ports = ArduinoDeviceManager.GetBoardPorts(SupportedBoards, AllowedPorts, DeniedPorts);

                    //build a list of any ADMs that are no longer connected to one of these ports (e.g. USB has been yanked out)
                    List<String> disconnect = new List<String>();
                    foreach (KeyValuePair<String, ArduinoDeviceManager> entry in ADMS)
                    {
                        if (!ports.Contains(entry.Key))
                        {
                            //if for some reason (config change) the port for the ADP is not a possible board port
                            disconnect.Add(entry.Key);
                        }
                        else
                        {
                            //otherwise this ADM is on a valid port so we try and assert the connection
                            try
                            {
                                //Tracing?.TraceEvent(TraceEventType.Information, 0, "ADM: Asserting connection");
                                entry.Value.AssertConnection();
                            }
                            catch (Exception e)
                            {
                                Tracing?.TraceEvent(TraceEventType.Error, 1000, "Assert connection failed: {0} -> {1}", e.GetType(), e.Message);
                                disconnect.Add(entry.Key);
                            }
                        }
                    }
                    
                    //now formally disconnect boards that have not been found on any port
                    foreach (String port in disconnect)
                    {
                        DisconnectADM(port);
                    }

                    //now we try and connect all the boards that we have not just disconnected but have not yet been connected
                    if (ports.Count > 0 && ADMS.Count < RequiredBoardsCount)
                    {
                        _noPortsFoundWarning = false;
                        foreach (String port in ports)
                        {
                            if (disconnect.Contains(port) || ADMS.ContainsKey(port)) continue;

                            ConnectADM(port);
                        }
                    }
                    else if(ADMS.Count < RequiredBoardsCount)
                    {
                        if (!_noPortsFoundWarning)
                        {
                            Tracing?.TraceEvent(TraceEventType.Warning, 100, "ADM: No boards connected to any port");
                            _noPortsFoundWarning = true;
                        }
                    }

                    //here we check how long it has been since the last 'ping' response and force a disconnect ... next time round it should reconnect
                    foreach (ArduinoDeviceManager adm in ADMS.Values)
                    {
                        if (adm.LastPingResponseMessage == null) continue;
                        long lastPing = (DateTime.Now.Ticks - adm.LastPingResponseOn.Ticks) / TimeSpan.TicksPerSecond;
                        if (lastPing > MaxPingResponseTime)
                        {
                            //this will get 'formally' disconnected the next timer event
                            Tracing?.TraceEvent(TraceEventType.Warning, 100, "ADM: Last ping for board {0} on port {1} occured {2} seconds ago so disconnecting...", adm.BoardID, adm.Port, lastPing);
                            DisconnectADM(adm.Port);
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
                Tracing?.TraceEvent(TraceEventType.Error, 100, "HandleADMMessage Error: {0}", e.Message);
            }
        }

        virtual protected void HandleADMMessage(ADMMessage message, ArduinoDeviceManager adm)
        {
            switch (message.Type)
            {
                case MessageType.ERROR:
                    Tracing?.TraceEvent(TraceEventType.Error, 100, "ADM {0} produced error: {1}", adm.BoardID == null ? "n/a" : adm.BoardID, message.Value);
                    break;

                case MessageType.WARNING:
                    Tracing?.TraceEvent(TraceEventType.Warning, 100, "ADM {0} produced warning: {1}", adm.BoardID == null ? "n/a" : adm.BoardID, message.Value);
                    break;

                case MessageType.STATUS_RESPONSE:
                    if(!IsRequiredBoard(adm.BoardID))
                    {
                        Tracing?.TraceEvent(TraceEventType.Warning, 100, "ADM {0} is not one of the required boards ({1}).  Disconnecting from port {2}...", adm.BoardID, RequiredBoards, adm.Port);
                        adm.Disconnect();
                        break;
                    }

                    if (adm.State == ADMState.DEVICE_READY)
                    {
                        Tracing?.TraceEvent(TraceEventType.Verbose, 100, "ADM: Ready to add devices to {0} on port {1} ...", adm.BoardID, adm.Port);
                        AddADMDevices(adm, message);
                        Tracing?.TraceEvent(TraceEventType.Verbose, 100, "ADM: {0} devices added to {1} on port {2}. Configuring devices... ", adm.DeviceCount, adm.BoardID, adm.Port);
                    }
                    break;

                case MessageType.CONFIGURE_RESPONSE:
                    if (adm.State == ADMState.DEVICE_CONNECTED && !_devicesConnected[adm.Port])
                    {
                        Tracing?.TraceEvent(TraceEventType.Verbose, 100, "ADM: All {0} devices now configured and connected to board {1} on port {2}", adm.DeviceCount, adm.BoardID, adm.Port);
                        OnADMDevicesConnected(adm, message);
                        _devicesConnected[adm.Port] = true;
                    }
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
            String msg = String.Format("All {0} added devices connected for {1} ", adm.DeviceCount, adm.BoardID);
            Broadcast(ADMEvent.DEVICES_CONNECTED, msg);
        }
    } //end class
}
