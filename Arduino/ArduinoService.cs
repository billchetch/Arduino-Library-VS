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
    public class ArduinoServiceMessage : ServiceMessage
    {
        public bool IsFirmata = false;
        public ADMStatus DeviceManagerStatus = ADMStatus.NOT_CONNECTED;

        public ArduinoServiceMessage()
        {
            //parameterless constructor required for xml serializing
        }

        public ArduinoServiceMessage(NamedPipeManager.MessageType type = NamedPipeManager.MessageType.NOT_SET) : base(type)
        {

        }

        public ArduinoServiceMessage(String message, int subType = 0, NamedPipeManager.MessageType type = NamedPipeManager.MessageType.NOT_SET) : base(message, subType, type)
        {

        }

        public ArduinoServiceMessage(String message, NamedPipeManager.MessageType type = NamedPipeManager.MessageType.NOT_SET) : this(message, 0, type)
        {
            //empty
        }
    }

    abstract public class ArduinoService : NamedPipeService<ArduinoServiceMessage>
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

        protected ArduinoServiceMessage CreateMessage()
        {
            var message = new ArduinoServiceMessage();
            message.DeviceManagerStatus = ADM.Status;
            return message;
        }

        virtual protected ArduinoServiceMessage CreateMessage(FirmataMessage firmataMessage)
        {
            var message = new ArduinoServiceMessage(NamedPipeManager.MessageType.CUSTOM);
            message.SubType = (int)firmataMessage.Type;
            message.IsFirmata = true;
            message.Add(firmataMessage.Value.ToString());
            return message;
        }

        protected override ArduinoServiceMessage CreatePingResponse(ArduinoServiceMessage message)
        {
            var response = base.CreatePingResponse(message);
            response.DeviceManagerStatus = ADM == null ? ADMStatus.NOT_CONNECTED : ADM.Status;
            response.Add(ADM.Status == ADMStatus.NOT_CONNECTED ? "ADM not connected" : "ADM connected");
            return response;
        }

        protected override ArduinoServiceMessage CreateStatusResponse(ArduinoServiceMessage message)
        {
            var response = base.CreateStatusResponse(message);
            response.DeviceManagerStatus = ADM == null ? ADMStatus.NOT_CONNECTED : ADM.Status;
            response.Add(ADM.Status == ADMStatus.NOT_CONNECTED ? "ADM not connected" : "ADM connected");
            return response;
        }

        public virtual void OnTimer(Object sender, ElapsedEventArgs eventArgs)
        {

            if (ADM == null)
            {
                try
                {
                    this.timer.Stop();
                    var message = CreateMessage();
                    message.Value = "ADM not connected...";
                    Broadcast(message);
                    ADM = ArduinoDeviceManager.Connect(SupportedBoards, this.OnADMFirmataMessage);
                }
                catch (Exception e)
                {
                    Log.WriteError(e.Message);
                    Broadcast(new ArduinoServiceMessage(e.Message, NamedPipeManager.MessageType.ERROR));
                }
                finally
                {
                    if (ADM != null)
                    {
                        //ADM connected to board
                        timer.Interval = 5000;
                        Log.WriteInfo("ADM connected ... checking for disconnect at intervals of " + timer.Interval + "ms");
                        var message = CreateMessage();
                        message.Value = "ADM connected!";
                        Broadcast(message);
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
                    Broadcast(new ArduinoServiceMessage(s, NamedPipeManager.MessageType.WARNING));
                }
                finally
                {
                    timer.Start();
                }
            }
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
