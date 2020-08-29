using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Timers;
using Chetch.Services;
using Chetch.Messaging;

namespace Chetch.Arduino
{
    abstract public class ADMService : TCPMessagingClient
    {
        public enum ADMEvent
        {
            CONNECTED,
            DISCONNECTED,
            DEVICES_CONNECTED
        }
        
        //map of port names to arduino device managers
        protected Dictionary<String, ArduinoDeviceManager> ADMS { get; } = new Dictionary<String, ArduinoDeviceManager>();
        protected String SupportedBoards { get; set; }
        protected Timer _admtimer;
        private Dictionary<String, bool> _devicesConnected = new Dictionary<string, bool>();
        private bool _noPortsFoundWarning = false; //has a no ports found warning been 'traced' ... a flag to prevent multiple trace/log entries
        private Object _lockMonitorADM = new Object(); //lock so we don't disconnect/connect concurrently

        abstract protected void AddADMDevices(ArduinoDeviceManager adm, ADMMessage message);
        

        public ADMService(String clientName, String clientManagerSource, String serviceSource, String eventLog) : base(clientName, clientManagerSource, serviceSource, eventLog)
        {
            //empty
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
                Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Starting ADM service");

                //create timer
                _admtimer = new System.Timers.Timer();
                _admtimer.Interval = 1000;
                _admtimer.Elapsed += new System.Timers.ElapsedEventHandler(this.MonitorADM);
                _admtimer.Start();
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
                        Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Disconnecting ADMs");
                        foreach(ArduinoDeviceManager adm in ADMS.Values){
                            adm.Disconnect();
                        }
                        Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Disconnected ADMs");
                    }
                }
            }
            base.OnStop();
        }

        public override void HandleClientError(Connection cnn, Exception e)
        {
            //throw new NotImplementedException();
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
            switch (command)
            {
                case "list-commands":
                    device = adm.GetDevice(deviceID);
                    response.AddValue("DeviceID", deviceID);
                    var cms = device.GetCommands();
                    response.AddValue("DeviceCommands", cms.Select(i => i.CommandAlias).ToList());
                    break;

                case "status":
                    device = adm.GetDevice(deviceID);
                    if(device.BoardID == 0)
                    {
                        throw new Exception(String.Format("Device {0} does not have a board ID", deviceID));
                    }
                    adm.RequestStatus(device.BoardID);
                    break;

                default:
                    var commands = command.Split(',');
                    foreach (var cmd in commands)
                    {
                        var tcmd = cmd.Trim();
                        if (tcmd.Equals("wait", StringComparison.OrdinalIgnoreCase))
                        {
                            System.Threading.Thread.Sleep(200);
                        }
                        else
                        {
                            adm.IssueCommand(deviceID, tcmd, args);
                        }
                    }
                    break;
            }
            return respond;
        }

        

        override public void AddCommandHelp(List<String> commandHelp)
        {
            base.AddCommandHelp(commandHelp);

            //general commands related to a service
            commandHelp.Add("status: Get status info about this service and the ADMs");

            //adm specific commands related to a board and device
            commandHelp.Add("adm/<board>:status:  ADM will request board status and add additional information");
            commandHelp.Add("adm/<board>:ping: ADM will ping the board <repeat?>");
            commandHelp.Add("adm/<board>:list-boards: List boards used by this service");
            commandHelp.Add("adm/<board>:list-devices: List devices added to ADM");
            commandHelp.Add("adm/<board>:capability: List pin capabilities");
            commandHelp.Add("adm/<board>:setdigitalpin: Set the <pin number> to <true/false>");
            commandHelp.Add("adm/<board>:<device>:wait: Will simply pause for a short while, useful if interspersed with other, comma-seperated, commands");
            commandHelp.Add("adm/<board>:<device>:list-commands: List device commands");
        }

        override public bool HandleCommand(Connection cnn, Message message, String cmd, List<Object> args, Message response)
        {
            bool respond = true;
            switch (cmd)
            {
                case "status":
                    if (ADMS != null && ADMS.Count > 0)
                    {
                        response.AddValue("ADMS", ADMS.Values.Select(i => String.Format("Board {0} on port {1} has state {2}, last error: {3}", i.BoardID, i.Port, i.State, i.LastErrorMessage == null ? "n/a" : i.LastErrorMessage.Value)).ToList());
                    } else
                    {
                        response.AddValue("ADMS", "No boards connected");
                    }
                    response.AddValue("Ports", ArduinoDeviceManager.GetBoardPorts(SupportedBoards));
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
                                adm.RequestStatus();
                                break;

                            case "ping":
                                repeat = args != null && args.Count > 0 ? System.Convert.ToInt16(args[0]) : 1;
                                delay = args != null && args.Count > 1 ? System.Convert.ToInt16(args[1]) : 1000;
                                for (int i = 0; i < repeat; i++)
                                {
                                    adm.Ping();
                                    System.Threading.Thread.Sleep(delay);
                                }
                                break;

                            case "pingloadtest":
                                repeat = args != null && args.Count > 0 ? System.Convert.ToInt16(args[0]) : 10;
                                delay = args != null && args.Count > 1 ? System.Convert.ToInt16(args[1]) : 500;
                                for (int i = 0; i < repeat; i++)
                                {
                                    adm.Ping();
                                    System.Threading.Thread.Sleep(delay);
                                }
                                break;

                            case "capability":
                                var lbc = adm.ListBoardCapability();
                                response.AddValue("PinCount: ", lbc.Count);
                                response.AddValue("Pins", lbc);
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
                                response.AddValue("Devices", adm.GetDevices().Select(i => i.ToString(true)).ToList());
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

        //ADM related

        /// <summary>
        /// Called by a timer
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        public virtual void MonitorADM(Object sender, ElapsedEventArgs eventArgs)
        {
            _admtimer.Stop();

            lock(_lockMonitorADM)
            {
                //get all current ports that have boards connected
                List<String> ports = ArduinoDeviceManager.GetBoardPorts(SupportedBoards);

                //build a list of any ADMs that are no longer connected to one of these ports
                List<String> disconnect = new List<String>();
                foreach (KeyValuePair<String, ArduinoDeviceManager> entry in ADMS)
                {
                    if (!ports.Contains(entry.Key))
                    {
                        disconnect.Add(entry.Key);
                    }
                    else
                    {
                        try
                        {
                            //Tracing?.TraceEvent(TraceEventType.Information, 0, "ADM: Asserting connection");
                            entry.Value.AssertConnection();
                        }
                        catch (Exception e)
                        {
                            disconnect.Add(entry.Key);
                        }
                    }
                }


                //now formally disconnect boards that have not been found on any port
                foreach (String key in disconnect)
                {
                    ArduinoDeviceManager adm = ADMS[key];
                    ADMS.Remove(key);
                    _devicesConnected.Remove(key);

                    Tracing?.TraceEvent(TraceEventType.Warning, 100, "ADM: Board {0} on port {1} disconnected", adm.BoardID, key);
                    Broadcast(ADMEvent.DISCONNECTED, String.Format("{0} disconnected from port {1}", adm.BoardID, key));
                }

                //now we try and connect all the boards that we have not just disconnected but have not yet been connected
                if (ports.Count > 0)
                {
                    _noPortsFoundWarning = false;
                    foreach (String key in ports)
                    {
                        if (disconnect.Contains(key) || ADMS.ContainsKey(key)) continue;

                        try
                        {
                            Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Attempting to connect board on port {0}", key);
                            ADMS[key] = ArduinoDeviceManager.Connect(key, TryHandleADMMessage);
                            _devicesConnected[key] = false;
                            Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Connected board on port {0}", key);
                            Broadcast(ADMEvent.CONNECTED, String.Format("Connected ADM to port {0}", key));
                        }
                        catch (Exception e)
                        {
                            Tracing?.TraceEvent(TraceEventType.Error, 100, "ADM: Failed to connect, exception {0}: {1}", e.GetType().ToString(), e.Message);
                        }
                    }
                }
                else
                {
                    if (!_noPortsFoundWarning)
                    {
                        Tracing?.TraceEvent(TraceEventType.Warning, 100, "ADM: No boards connected to any port");
                        _noPortsFoundWarning = true;
                    }
                }
            }
            _admtimer.Start();
        }

        //messaging
        protected ADMMessage CreateMessage(MessageType mtype)
        {
            var message = new ADMMessage();
            message.Type = mtype;
            return message;
        }

        protected ADMMessage CreateNotification(ADMEvent admEvent, String msg = null)
        {
            var message = CreateMessage(MessageType.NOTIFICATION);
            message.SubType = (int)Server.NotificationEvent.CUSTOM;
            message.AddValue("ADMEvent", admEvent);
            message.Value = msg;

            return message;
        }

        protected void Broadcast(ADMEvent admEvent, String msg = null)
        {
            ADMMessage message = CreateNotification(admEvent, msg);
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
                    Tracing?.TraceEvent(TraceEventType.Error, 0, "ADM {0} produced error: {1}", adm.BoardID == null ? "n/a" : adm.BoardID, message.Value);
                    break;

                case MessageType.WARNING:
                    Tracing?.TraceEvent(TraceEventType.Warning, 0, "ADM {0} produced warning: {1}", adm.BoardID == null ? "n/a" : adm.BoardID, message.Value);
                    break;

                case MessageType.STATUS_RESPONSE:
                    if (adm.State == ADMState.DEVICE_READY)
                    {
                        Tracing?.TraceEvent(TraceEventType.Verbose, 100, "ADM: Ready to add devices to {0} ...", adm.BoardID);
                        AddADMDevices(adm, message);
                        Tracing?.TraceEvent(TraceEventType.Verbose, 100, "ADM: {0} devices added to {1}. Configuring devices... ", adm.DeviceCount, adm.BoardID);
                    }
                    break;

                case MessageType.CONFIGURE_RESPONSE:
                    if (adm.State == ADMState.DEVICE_CONNECTED && !_devicesConnected[adm.Port])
                    {
                        Tracing?.TraceEvent(TraceEventType.Verbose, 100, "ADM: All {0} devices now configured and connected to board {1}", adm.DeviceCount, adm.BoardID);
                        OnADMDevicesConnected(adm, message);
                        _devicesConnected[adm.Port] = true;
                    }
                    break;

            }

            
            message.AddValue("BoardID", adm.BoardID);
            ArduinoDevice dev = null;
            if (message.TargetID > 0)
            {
                dev = adm.GetDeviceByBoardID(message.TargetID);
            }else if(message.Sender != null && message.Sender != String.Empty)
            {
                dev = adm.GetDevice(message.Sender);
            }
            message.AddValue("DeviceID", dev != null ? dev.ID : "");
            Broadcast(message);
        }

        virtual protected void OnADMDevicesConnected(ArduinoDeviceManager adm, ADMMessage message)
        {
            String msg = String.Format("All {0} added devices connected for {1} ", adm.DeviceCount, adm.BoardID);
            Broadcast(ADMEvent.DEVICES_CONNECTED, msg);
        }
    } //end class
}
