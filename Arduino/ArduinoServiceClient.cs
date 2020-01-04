using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace Chetch.Arduino
{
    abstract public class ArduinoServiceClient : IDisposable
    {
        private const int CONNECT_TIMEOUT = 5000;

        private String _serviceName;
        private NamedPipeClientStream _pipeClientIn;
        private NamedPipeClientStream _pipeClientOut;
        private StreamWriter _sw;

        public ArduinoServiceClient(String serviceName)
        {
            _serviceName = serviceName;

            _pipeClientIn = new NamedPipeClientStream(".", _serviceName + "PipeOut", PipeDirection.In);
            _pipeClientOut = new NamedPipeClientStream(".", _serviceName + "PipeIn", PipeDirection.Out);

        }

        public void StartListening(Action<Task> OnStoppedListening)
        {
            Task task = new Task(this.Listen);
            task.ContinueWith(OnStoppedListening);
            task.Start();
        }

        private void Listen()
        {
            if (!_pipeClientIn.IsConnected)
            {
                _pipeClientIn.Connect(CONNECT_TIMEOUT);
            }

            using (StreamReader sr = new StreamReader(_pipeClientIn))
            {
                while (_pipeClientIn.IsConnected)
                {
                    String temp;
                    while ((temp = sr.ReadLine()) != null)
                    {
                        OnMessageReceived(temp);
                    }
                    Thread.Sleep(100);
                }
            }
        }

        abstract public void OnMessageReceived(String message);

        public void Send(String data)
        {
            if (!_pipeClientOut.IsConnected)
            {
                _pipeClientOut.Connect(CONNECT_TIMEOUT);
            }

            if (_sw == null)
            {
                _sw = new StreamWriter(_pipeClientOut);
                _sw.AutoFlush = true;
            }

            _sw.WriteLine(data);
        }


        public void Dispose()
        {
            _pipeClientOut.Close();
            _pipeClientIn.Dispose();
            _pipeClientIn = null;

            if (_sw != null)
            {
                _sw.Close();
                _sw.Dispose();
                _sw = null;
            }
            _pipeClientOut.Close();
            _pipeClientOut.Dispose();
            _pipeClientOut = null;
        }
    }
}
