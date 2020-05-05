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


        public class ADMListener
        {
            public static MessageType[] DefaultMessageTypes { get; } = new MessageType[] { MessageType.NOTIFICATION, MessageType.STATUS_RESPONSE, MessageType.INFO, MessageType.WARNING, MessageType.ERROR, MessageType.DATA };

            public String ClientName { get; internal set; }

            protected Dictionary<String, List<MessageType>> Targets { get; } = new Dictionary<String, List<MessageType>>();


            public ADMListener(String clientName)
            {
                ClientName = clientName;
            }

            public void AddTarget(String target, MessageType messageType)
            {
                var mt = new List<MessageType>();
                mt.Add(messageType);
                AddTarget(target, mt);
            }

            public void AddTarget(String target, List<MessageType> messageTypes = null)
            {
                if (!Targets.ContainsKey(target))
                {
                    var mt = new List<MessageType>(DefaultMessageTypes);
                    if (messageTypes != null && messageTypes.Count > 0)
                    {
                        mt.AddRange(messageTypes);
                    }
                    Targets[target] = mt;
                }
            }

            public bool HasTarget(String target)
            {
                return Targets.ContainsKey(target);
            }

            public List<MessageType> GetTargetTypes(String target)
            {
                if (HasTarget(target))
                {
                    return Targets[target];
                } else
                {
                    return null;
                }
            }

            public bool CanReceive(ADMMessage message)
            {
                if(message.Sender == null || !HasTarget(message.Sender))
                {
                    return false;
                } else
                {
                    var mts = GetTargetTypes(message.Sender);
                    return (mts != null && mts.Contains(message.Type));
                }
            }

            public override string ToString()
            {
                var tgts = String.Join(", ", Targets.Keys.ToList());
                return String.Format("{0} is listening to {1}", ClientName, tgts);
            }
        }


        protected ArduinoDeviceManager ADM { get; set; }
        protected String SupportedBoards { get; set; }
        protected Timer _admtimer;
        private bool _devicesAdded = false;
        private bool _devicesConnected = false;

        protected Dictionary<String, ADMListener> Listeners { get; } = new Dictionary<string, ADMListener>();

        abstract protected void AddADMDevices(ADMMessage message);
        

        public ADMService(String clientName, String clientManagerSource, String serviceSource, String eventLog) : base(clientName, clientManagerSource, serviceSource, eventLog)
        {
            //empty
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
                _admtimer.Stop();
                if (ADM != null)
                {
                    Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Disconnecting ADM");
                    ADM.Disconnect();
                    Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Disconnected");
                }
            }
            base.OnStop();
        }

        public override void HandleClientError(Connection cnn, Exception e)
        {
            //throw new NotImplementedException();
        }

        public override void HendleClientMessage(Connection cnn, Message message)
        {
            base.HendleClientMessage(cnn, message);
        }

        virtual protected void HandleADMCommand(String deviceID, String command, List<Object> args, Message response)
        {
            if (!ADM.HasDevice(deviceID))
            {
                throw new Exception(String.Format("Device {0} has not been added to ADM", deviceID));
            }

            ArduinoDevice device = null;
            switch (command)
            {
                case "list-commands":
                    device = ADM.GetDevice(deviceID);
                    response.AddValue("DeviceID", deviceID);
                    var cms = device.GetCommands();
                    response.AddValue("DeviceCommands", cms.Select(i => i.CommandAlias).ToList());
                    break;

                case "status":
                    device = ADM.GetDevice(deviceID);
                    if(device.BoardID == 0)
                    {
                        throw new Exception(String.Format("Device {0} does not have a board ID", deviceID));
                    }
                    ADM.RequestStatus(device.BoardID);
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
                            ADM.IssueCommand(deviceID, tcmd, args);
                        }
                    }
                    break;
            }
        }

        virtual protected void RegisterListener(String clientName, String targets, List<MessageType> messageTypes = null)
        {
            var listener = new ADMListener(clientName);
            var tgts2add = new List<String>();
            if (targets == null)
            {
                tgts2add.Add("BOARD");
            }
            else
            {
                tgts2add.AddRange(targets.Split(','));
            }

            foreach (var tgt in tgts2add)
            {
                var ttgt = tgt.Trim();
                listener.AddTarget(ttgt, messageTypes);

                var mts = listener.GetTargetTypes(ttgt);
                if (mts != null)
                {
                    var types = String.Join(", ", mts.Select(i => i.ToString()).ToList());
                    Tracing?.TraceEvent(TraceEventType.Information, 100, "Registered {0} to listen to {1} for {2}", clientName, ttgt, types);
                }
            }
            Listeners[clientName] = listener;
        }

        virtual protected void DeregisterListener(String clientName)
        {
            if (Listeners.ContainsKey(clientName))
            {
                Listeners.Remove(clientName);
                Tracing?.TraceEvent(TraceEventType.Information, 100, "Deregistered listener {0}", clientName);
            }
        }


        override public void AddCommandHelp(List<String> commandHelp)
        {
            base.AddCommandHelp(commandHelp);

            commandHelp.Add("listen/register: Register this client to receive messages from ADM <board/devices...?> additional <message type?>. ");
            commandHelp.Add("unlisten/deregister: Deregister this client to receive messages from ADM");
            commandHelp.Add("status: Get status info about this service and the ADM");
            commandHelp.Add("adm:status:  ADM will request board status and add additional information");
            commandHelp.Add("adm:ping: ADM will ping the board <repeat?>");
            commandHelp.Add("adm:blink: ADM will blink the built in LED");
            commandHelp.Add("adm:list-devices: List devices added to ADM");
            commandHelp.Add("adm:capability: List pin capabilities");
            commandHelp.Add("adm:setdigitalpin: Set the <pin number> to <true/false>");
            commandHelp.Add("adm:<device>:wait: Will simply pause fora short while, useful if interspersed with other commands");
            commandHelp.Add("adm:<device>:list-commands: List device commands");
        }

        override public bool HandleCommand(Connection cnn, Message message, String cmd, List<Object> args, Message response)
        {
            switch (cmd)
            {
                case "listen":
                case "register":
                    String targets = args == null || args.Count == 0 ? null : args[0].ToString();

                    if (message.Sender != null)
                    {
                        List<MessageType> mts = null;
                        if(args != null && args.Count > 1)
                        { 
                            mts = new List<MessageType>();
                            for (int i = 1; i < args.Count; i++)
                            {
                                MessageType mt = (MessageType)System.Convert.ToInt16(args[i]);
                                mts.Add(mt);
                            }
                        }
                        RegisterListener(message.Sender, targets, mts);
                        response.Value = String.Format("Registered {0} to listen for messages from {1} ", message.Sender, targets);
                    }
                    else
                    {
                        response.Type = MessageType.ERROR;
                        response.Value = "Only named clients can register";
                        return true;
                    }
                    break;

                case "unlisten":
                case "deregister":
                    if (message.Sender != null)
                    {
                        DeregisterListener(message.Sender);
                        response.Value = String.Format("Unregistered {0}", message.Sender);
                    }
                    break;

                case "status":
                    response.AddValue("ADMState", ADM != null ? ADM.State.ToString() : "Not created");
                    if (ADM != null)
                    {
                        var devs = ADM.GetDevices();
                        response.AddValue("DeviceCount", devs.Count);
                        response.AddValue("Devices", devs.Select(i => i.ToString()).ToList());
                    }
                    response.AddValue("ListenerCount", Listeners.Count);
                    response.AddValue("Listeners", Listeners.Values.Select(i => i.ToString()).ToList());
                    break;

                default:
                    var tgtcmd = cmd.Split(':');
                    if (tgtcmd.Length >= 2 && tgtcmd[0].Equals("ADM", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ADM == null || ADM.State == ADMState.NOT_CONNECTED || ADM.State == ADMState.CONNECTING)
                        {
                            response.Type = MessageType.ERROR;
                            response.Value = "ADM is not connected";
                            return true;
                        }

                        //handle commands related to the board (i.e. not to a specific added device)
                        if (tgtcmd.Length == 2)
                        {
                            int repeat; //frequently used var name
                            int delay; //frequently used var name
                            switch (tgtcmd[1].ToLower())
                            {
                                case "status":
                                    if (Listeners.ContainsKey(message.Sender) && Listeners[message.Sender].HasTarget("BOARD"))
                                    {
                                        ADM.RequestStatus();
                                    } else
                                    {
                                        throw new Exception("Cannot receive status response as not listening for BOARD messages");
                                    }
                                    
                                    break;

                                case "ping":
                                    if (Listeners.ContainsKey(message.Sender) && Listeners[message.Sender].HasTarget("BOARD"))
                                    {
                                        repeat = args != null && args.Count > 0 ? System.Convert.ToInt16(args[0]) : 1;
                                        delay = args != null && args.Count > 1 ? System.Convert.ToInt16(args[1]) : 1000;
                                        for (int i = 0; i < repeat; i++)
                                        {
                                            ADM.Ping();
                                            System.Threading.Thread.Sleep(delay);
                                        }
                                    }
                                    else
                                    {
                                        throw new Exception("Cannot receive ping response as not listening for BOARD messages");
                                    }
                                    break;

                                case "pingloadtest":
                                    repeat = args != null && args.Count > 0 ? System.Convert.ToInt16(args[0]) : 10;
                                    delay = args != null && args.Count > 1 ? System.Convert.ToInt16(args[1]) : 500;
                                    for (int i = 0; i < repeat; i++)
                                    {
                                        ADM.Ping();
                                        System.Threading.Thread.Sleep(delay);
                                    }
                                    break;

                                case "blink":
                                    repeat = args != null && args.Count > 0 ? System.Convert.ToInt16(args[0]) : 10;
                                    delay = args != null && args.Count > 1 ? System.Convert.ToInt16(args[1]) : 200;
                                    ADM.Blink(repeat, delay);
                                    break;

                                case "capability":
                                    var lbc = ADM.ListBoardCapability();
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
                                    if(!ADM.IsPinCapable(pin, Solid.Arduino.Firmata.PinMode.DigitalOutput))
                                    {
                                        throw new Exception(String.Format("Pin {0} is not capabale of digital output", pin));
                                    }
                                    if(ADM.GetDevicesByPin(pin) != null)
                                    {
                                        throw new Exception(String.Format("Pin {0} is being used by a device", pin));
                                    }
                                    ADM.SetDigitalPinMode(pin, Solid.Arduino.Firmata.PinMode.DigitalOutput);
                                    ADM.SetDigitalPin(pin, val);
                                    break;

                                case "list-devices":
                                    response.AddValue("Devices", ADM.GetDevices().Select(i => i.ToString()).ToList());
                                    break;

                                default:
                                    throw new Exception(String.Format("No ADM direct command {0}", tgtcmd[1]));
                                    break;
                            }
                        }
                        else
                        {
                            //handle demand specific to device
                            try
                            {
                                HandleADMCommand(tgtcmd[1], tgtcmd[2], args, response);
                            } catch (Exception e)
                            {
                               Tracing?.TraceEvent(TraceEventType.Error, 0, "Exception: {0}", e.Message);
                               throw e;
                            }
                        }

                        response.Value = "Handled " + cmd;
                    }
                    else
                    {
                        Tracing?.TraceEvent(TraceEventType.Warning, 100, "ADM: Unrecognised command {0}", cmd);
                        throw new Exception(String.Format("ADM: Unrecognised command {0}", cmd));
                    }
                    break;
            }

            return true;
        }

        //ADM related

        /// <summary>
        /// Called by a timer
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        public virtual void MonitorADM(Object sender, ElapsedEventArgs eventArgs)
        {
            if (ADM == null)
            {
                try
                {
                    _admtimer.Stop();
                    Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Attempting to connect ... looking for boards {0}", SupportedBoards);

                    ADM = ArduinoDeviceManager.Connect(SupportedBoards, HandleADMMessage);
                    ADM.Tracing = Tracing;
                }
                catch (Exception e)
                {
                    //problem creating ADM
                    Tracing?.TraceEvent(TraceEventType.Error, 100, "ADM: Failed to connect, exception {0}: {1}", e.GetType().ToString(), e.Message);
                }
                finally
                {
                    if (ADM != null)
                    {
                        //ADM connected to board
                        Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Connected!  Monitoring at intervals of {0}", _admtimer.Interval);
                        Notify(ADMEvent.CONNECTED, "ADM connected!");
                        Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Notified Listeners");

                    }
                    _admtimer.Interval = 5000;
                    _admtimer.Start();
                }
            }
            else
            {
                _admtimer.Stop();
                var priorState = ADM.State;
                try
                {
                    ADM.AssertConnection();
                }
                catch (Exception e)
                {
                    //ADM disconnected
                    ADM = null;
                    _devicesAdded = false;
                    _admtimer.Interval = 5000;
                    if (priorState != ADMState.NOT_CONNECTED)
                    {
                        var msg = String.Format("ADM: ADM disconnected due to exception {0}: {1}", e.GetType().ToString(), e.Message);
                        Notify(ADMEvent.DISCONNECTED, msg);
                        Tracing?.TraceEvent(TraceEventType.Error, 100, msg);
                        Tracing?.TraceEvent(TraceEventType.Information, 100, "ADM: Checking for reconnect at intervals of {0}", _admtimer.Interval);
                    }
                }
                finally
                {
                    _admtimer.Start();
                }
            }
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

        protected void Notify(ADMEvent admEvent, String msg = null)
        {
            ADMMessage message = CreateNotification(admEvent, msg);
            Client.SendMessage(message);
        }

        protected void SendToListeners(ADMMessage message)
        {
            foreach (var listener in Listeners.Values)
            {
                if (listener.CanReceive(message))
                {
                    message.Target = listener.ClientName;
                    Client.SendMessage(message);
                }
            }
        }

        virtual protected void HandleADMMessage(ADMMessage message, ArduinoDeviceManager Mgr)
        {
            if (Mgr != ADM)
            {
                Tracing?.TraceEvent(TraceEventType.Error, 100, "ADM: Mgr is not the same instance as ADM");
                return;
            }

            if (!_devicesAdded && ADM.State == ADMState.DEVICE_READY)
            {
                Tracing?.TraceEvent(TraceEventType.Verbose, 100, "ADM: Ready to add devices...");
                AddADMDevices(message);
                Tracing?.TraceEvent(TraceEventType.Verbose, 100, "ADM: Added devices");
                _devicesAdded = true;
            }

            if (!_devicesConnected && ADM.State == ADMState.DEVICE_CONNECTED)
            {
                Tracing?.TraceEvent(TraceEventType.Verbose, 100, "ADM: Devices connected...");
                OnADMDevicesConnected(message);
                _devicesConnected = true;
            }

            String sender = null;
            if (message.TargetID == 0)
            {
                sender = "BOARD";
            }
            else
            {
                var dev = ADM.GetDeviceByBoardID(message.TargetID);
                if (dev != null)
                {
                    sender = dev.ID;
                }
            }

            if (sender != null)
            {
                message.Sender = sender;
                SendToListeners(message);
            }
        }

        virtual protected void OnADMDevicesConnected(ADMMessage message)
        {
            Notify(ADMEvent.DEVICES_CONNECTED, "All added devices connected");
        }
    } //end class
}
