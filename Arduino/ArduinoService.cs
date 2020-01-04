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
        private StreamWriter _sw;

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
            ClearPipeIn();
            ClearPipeOut();
            Log.WriteEntry("Serivce stopped", EventLogEntryType.Information);
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
                }
                finally
                {
                    if (ADM != null)
                    {
                        //ADM connected to board
                        timer.Interval = 5000;
                        Log.WriteEntry("ADM connected ... checking for disconnect at intervals of " + timer.Interval + "ms", EventLogEntryType.Information);
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
                    Log.WriteEntry("ADM disconnected ... checking for reconnect at intervals of " + timer.Interval + "ms", EventLogEntryType.Information);
                    Broadcast("ADM disconnected");
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
            Broadcast(message.Value.ToString());
        }

        protected bool Broadcast(String data)
        {
            if (_pipeServerOut.IsConnected)
            {
                try
                {
                    if (_sw == null)
                    {
                        _sw = new StreamWriter(_pipeServerOut);
                        _sw.AutoFlush = true;
                    }

                    _sw.WriteLine(data);
                    return true;
                }
                catch (Exception e)
                {
                    Log.WriteEntry(e.Message, EventLogEntryType.Error);
                }
            }
            return false;
        }

        private int OnClientConnectOutbound(NamedPipeServerStream stream)
        {
            try
            {
                if (stream != _pipeServerOut)
                {
                    ClearPipeOut(stream);
                }

                while (stream.IsConnected)
                {
                    //just wait...
                    System.Threading.Thread.Sleep(100);
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

        //inbound connections
        virtual protected void OnClientMessageReceived(String message)
        {
            Log.WriteEntry(message, EventLogEntryType.Information);
        }

        private int OnClientConnectInbound(NamedPipeServerStream stream)
        {
            try
            {
                if (stream != _pipeServerIn)
                {
                    ClearPipeIn(stream);
                }

                using (StreamReader sr = new StreamReader(stream))
                {
                    while (stream.IsConnected)
                    {
                        // Display the read text to the console
                        string temp;
                        while ((temp = sr.ReadLine()) != null)
                        {
                            OnClientMessageReceived(temp);
                        }
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
        
        protected void ClearPipeOut(NamedPipeServerStream newPipe = null)
        {
            _sw = null;
            _pipeServerOut.Close();
            _pipeServerOut.Dispose();
            _pipeServerOut = newPipe;
        }

        protected void ClearPipeIn(NamedPipeServerStream newPipe = null)
        {
            _pipeServerIn.Close();
            _pipeServerIn.Dispose();
            _pipeServerIn = newPipe;
        }
    }
}
