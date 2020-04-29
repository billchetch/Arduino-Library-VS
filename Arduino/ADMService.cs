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

            public void AddTarget(String target)
            {
                if (!Targets.ContainsKey(target))
                {
                    Targets[target] = new List<MessageType>(DefaultMessageTypes);
                }
            }

            public bool CanReceive(ADMMessage message)
            {
                return message.Sender == null ? false : Targets.ContainsKey(message.Sender);
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

        protected Dictionary<String, ADMListener> Listeners { get; } = new Dictionary<string, ADMListener>();

        abstract protected void AddADMDevices();


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

        virtual protected void HandleADMCommand(String deviceID, String command, List<Object> args)
        {
            ADM.IssueCommand(deviceID, command, args);
        }

        virtual protected void RegisterListener(String clientName, String targets)
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
                listener.AddTarget(tgt.Trim());
            }
            Listeners[clientName] = listener;
            Tracing?.TraceEvent(TraceEventType.Information, 100, "Registered listener {0}", listener.ToString());
        }

        virtual protected void DeregisterListener(String clientName)
        {
            if (Listeners.ContainsKey(clientName))
            {
                Listeners.Remove(clientName);
                Tracing?.TraceEvent(TraceEventType.Information, 100, "Deregistered listener {0}", clientName);
            }
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
                        RegisterListener(message.Sender, targets);
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
                    response.AddValue("ADMStatus", ADM != null ? ADM.Status.ToString() : "Not created");
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
                        if (ADM == null || ADM.Status == ADMStatus.NOT_CONNECTED || ADM.Status == ADMStatus.CONNECTING)
                        {
                            response.Type = MessageType.ERROR;
                            response.Value = "ADM is not connected";
                            return true;
                        }

                        if (tgtcmd.Length == 2)
                        {
                            switch (tgtcmd[1].ToLower())
                            {
                                case "status":
                                    ADM.RequestStatus();
                                    break;

                                case "ping":
                                    ADM.Ping();
                                    break;

                                case "blink":
                                    int repeat = args != null && args.Count > 0 ? System.Convert.ToInt16(args[0]) : 10;
                                    int delay = args != null && args.Count > 1 ? System.Convert.ToInt16(args[1]) : 100;
                                    ADM.Blink(repeat, delay);
                                    break;

                                default:
                                    break;
                            }
                        }
                        else
                        {
                            HandleADMCommand(tgtcmd[1], tgtcmd[2], args);
                        }

                        response.Value = "Handled " + cmd;
                    }
                    else
                    {
                        Tracing?.TraceEvent(TraceEventType.Warning, 100, "ADM: Unrecognised command {0}", cmd);
                        response.Value = "ADM: Unrecognised command {0}";
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
                var priorStatus = ADM.Status;
                try
                {
                    ADM.AssertConnection();
                }
                catch (Exception e)
                {
                    //ADM disconnected
                    ADM = null;
                    _admtimer.Interval = 5000;
                    if (priorStatus != ADMStatus.NOT_CONNECTED)
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

            if (!_devicesAdded && ADM.Status == ADMStatus.DEVICE_READY)
            {
                Tracing?.TraceEvent(TraceEventType.Verbose, 100, "ADM: Ready to add devices...");
                AddADMDevices();
                Tracing?.TraceEvent(TraceEventType.Verbose, 100, "ADM: Added devices");
                _devicesAdded = true;
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
    }
}
