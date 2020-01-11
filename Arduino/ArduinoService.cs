﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ServiceProcess;
using System.IO;
using System.IO.Pipes;
using Chetch.Utilities;
using System.Diagnostics;
using System.Timers;
using Solid.Arduino.Firmata;
using Chetch.Services;

namespace Chetch.Arduino
{
    abstract public class ArduinoService : NamedPipeService
    {
        protected ArduinoDeviceManager ADM { get; set; }
        protected String SupportedBoards { get; set; }
        protected Timer timer;
        
        public ArduinoService(String inboundID) : base(inboundID)
        {
            //empty
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                if(SupportedBoards == null)
                {
                    throw new Exception("Cannot run service if no supported boards specified");
                }

                //create timer
                timer = new Timer();
                timer.Interval = 1000;
                timer.Elapsed += new ElapsedEventHandler(this.OnTimer);
                timer.Start();
                Log.WriteInfo("Service started with timer at intervals " + timer.Interval);
            }
            catch (Exception e)
            {
                Log.WriteError(e.Message);
            }
        }

        public virtual void OnTimer(Object sender, ElapsedEventArgs eventArgs)
        {

            if (ADM == null)
            {
                try
                {
                    this.timer.Stop();
                    Broadcast("Looking for ADM...");
                    ADM = ArduinoDeviceManager.Connect(SupportedBoards, this.OnADMFirmataMessage);
                }
                catch (Exception e)
                {
                    Log.WriteEntry(e.Message, EventLogEntryType.Error);
                    Broadcast(new NamedPipeManager.Message(e.Message, NamedPipeManager.MessageType.ERROR));
                }
                finally
                {
                    if (ADM != null)
                    {
                        //ADM connected to board
                        timer.Interval = 5000;
                        Log.WriteInfo("ADM connected ... checking for disconnect at intervals of " + timer.Interval + "ms");
                        Broadcast("ADM connected");
                    }
                    timer.Start();
                }
            }
            else
            {
                timer.Stop();
                try
                {
                    ADM.AssertConnection();
                }
                catch (Exception)
                {
                    //ADM disconnected
                    ADM = null;
                    timer.Interval = 1000;
                    var s = "ADM disconnected ... checking for reconnect at intervals of " + timer.Interval + "ms";
                    Log.WriteWarning(s);
                    Broadcast(new NamedPipeManager.Message(s, NamedPipeManager.MessageType.WARNING));
                }
                finally
                {
                    timer.Start();
                }
            }
        }

        virtual protected NamedPipeManager.Message CreateMessage(FirmataMessage firmataMessage)
        {
            var message = new NamedPipeManager.Message(NamedPipeManager.MessageType.CUSTOM);
            message.SubType = (int)firmataMessage.Type;
            message.Add(firmataMessage.Value.ToString());
            return message;
        }

        protected override NamedPipeManager.Message CreatePingResponse(NamedPipeManager.Message message)
        {
            var response = base.CreatePingResponse(message);
            response.Add(ADM == null ? "ADM not connected" : "ADM connected");
            return response;
        }

        public void Send(FirmataMessage firmataMessage, String pipeName)
        {
            Send(CreateMessage(firmataMessage), pipeName);
        }

        public void Broadcast(FirmataMessage firmataMessage)
        {
            Broadcast(CreateMessage(firmataMessage));
        }

        //outbound connections
        virtual protected void OnADMFirmataMessage(FirmataMessage message)
        {
            Broadcast(message);
        }
    }
}
