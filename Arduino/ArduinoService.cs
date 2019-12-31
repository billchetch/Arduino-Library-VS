using System;
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

namespace Chetch.Arduino
{
    abstract public class ArduinoService : ServiceBase
    {
        protected ArduinoDeviceManager ADM { get; set; }
        protected String SupportedBoards { get; set; }
        protected EventLog Log { get; set; }
        protected Timer timer;
        private NamedPipeServerStream _pipeServerIn;
        private NamedPipeServerStream _pipeServerOut;

        public ArduinoService()
        {
            Log = new EventLog();
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                //create an inbound named pipe
                String pipeName = ServiceName + "PipeIn";
                PipeSecurity security = NamedPipeManager.GetSecurity(NamedPipeManager.SECURITY_EVERYONE);
                _pipeServerIn = NamedPipeManager.Create(pipeName, PipeDirection.In, security, this.OnClientConnectInbound);
                if (_pipeServerIn == null) throw new Exception("Failed to create inbound pipe server " + pipeName);
                Log.WriteEntry("Created inbound pipe: " + pipeName, EventLogEntryType.Information);

                //create an outbound named pipe
                pipeName = ServiceName + "PipeOut";
                _pipeServerOut = NamedPipeManager.Create(pipeName, PipeDirection.Out, security, this.OnClientConnectOutbound);
                if (_pipeServerOut == null) throw new Exception("Failed to create outbound pipe server " + pipeName);
                Log.WriteEntry("Created outbound pipe: " + pipeName, EventLogEntryType.Information);


                //create timer
                timer = new Timer();
                timer.Interval = 1000;
                timer.Elapsed += new ElapsedEventHandler(this.OnTimer);
                timer.Start();
                Log.WriteEntry("Timer created with interval " + timer.Interval, EventLogEntryType.Information);

                Log.WriteEntry("Serivce started", EventLogEntryType.Information);
            }
            catch (Exception e)
            {
                Log.WriteEntry(e.Message, EventLogEntryType.Error);
            }
        }

        protected override void OnStop()
        {
            Log.WriteEntry("Serivce stopped", EventLogEntryType.Information);
        }

        public void OnTimer(Object sender, ElapsedEventArgs eventArgs)
        {

            if (ADM == null)
            {
                try
                {
                    this.timer.Stop();
                    WriteToClient("Connecting ADM...");
                    ADM = ArduinoDeviceManager.Connect(SupportedBoards, this.OnADMFirmataMessage);
                    WriteToClient("ADM connected");
                }
                catch (Exception e)
                {
                    Log.WriteEntry(e.Message, EventLogEntryType.Error);
                }
                finally
                {
                    if (ADM != null)
                    {
                        //ADM connected to board
                        timer.Interval = 5000;
                        Log.WriteEntry("ADM connected ... checking for disconnect at intervals of " + timer.Interval + "ms", EventLogEntryType.Information);
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
                    Log.WriteEntry("ADM disconnected ... checking for reconnect at intervals of " + timer.Interval + "ms", EventLogEntryType.Information);
                    WriteToClient("ADM disconnected");
                }
                finally
                {
                    timer.Start();
                }
            }
        }

        //outbound connections
        virtual protected void OnADMFirmataMessage(FirmataMessage message)
        {
            WriteToClient(message.Value.ToString());
        }

        protected bool WriteToClient(String data)
        {
            if (_pipeServerOut.IsConnected)
            {
                try
                {

                    StreamWriter sw = new StreamWriter(_pipeServerOut);
                    sw.AutoFlush = true;
                    sw.Write(data);
                    return true;
                } catch (IOException)
                {
                    //pipe connectin has unexpectedly closed
                }
            }
            return false;
        }

        //inbound connections
        virtual protected void OnClientMessageReceived(String message)
        {
            Log.WriteEntry(message, EventLogEntryType.Information);
        }

        private int OnClientConnectInbound(NamedPipeServerStream stream)
        {
            try
            {
                StreamReader sr = new StreamReader(stream);
                while(stream.IsConnected){
                    // Display the read text to the console
                    string temp;
                    while ((temp = sr.ReadLine()) != null)
                    {
                        OnClientMessageReceived(temp);
                    }
                }
                return NamedPipeManager.WAIT_FOR_NEXT_CONNECTION;
            }
            // Catch the IOException that is raised if the pipe is broken
            // or disconnected.
            catch (IOException e)
            {
                Log.WriteEntry(e.Message, EventLogEntryType.Error);
                return NamedPipeManager.WAIT_FOR_NEXT_CONNECTION;
            }
        }

        private int OnClientConnectOutbound(NamedPipeServerStream stream)
        {
            try
            {
                while (stream.IsConnected)
                {
                    //just wait...
                    System.Threading.Thread.Sleep(1000);
                }
                return NamedPipeManager.WAIT_FOR_NEXT_CONNECTION;
            }
            // Catch the IOException that is raised if the pipe is broken
            // or disconnected.
            catch (IOException e)
            {
                Log.WriteEntry(e.Message, EventLogEntryType.Error);
                return NamedPipeManager.WAIT_FOR_NEXT_CONNECTION;
            }
        }
    }
}
