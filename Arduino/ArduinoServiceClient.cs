using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Pipes;

namespace Chetch.Arduino
{
    abstract class ArduinoServiceClient
    {
        private String _serviceName;
        private NamedPipeClientStream _pipeClientIn;
        private NamedPipeClientStream _pipeClientOut;


        ArduinoServiceClient(String serviceName)
        {
            _serviceName = serviceName;

            _pipeClientIn = new NamedPipeClientStream(".", _serviceName + "In", PipeDirection.In);
            _pipeClientOut = new NamedPipeClientStream(".", _serviceName + "Out", PipeDirection.Out);

        }

        public void Listen()
        {
            if (!_pipeClientIn.IsConnected)
            {
                _pipeClientIn.Connect();
            }

            try
            {
                StreamReader sr = new StreamReader(_pipeClientIn);
                String temp;
                while ((temp = sr.ReadLine()) != null)
                {
                    OnMessageReceived(temp);
                }
            } catch (IOException)
            {
                //means pipe has been closed
            }
        }

        abstract public void OnMessageReceived(String message);
        
        public bool Send(String message)
        {
            if (!_pipeClientOut.IsConnected)
            {
                _pipeClientOut.Connect();
            }

            try
            {
                StreamWriter sw = new StreamWriter(_pipeClientOut);
                sw.AutoFlush = true;
                sw.WriteLine(message);
                return true;
            } catch (IOException)
            {
                //means pipe has been closed
            }
            return false;
        }

        public void Dispose()
        {
            _pipeClientIn.Dispose();
            _pipeClientIn.Close();
            _pipeClientIn = null;

            _pipeClientOut.Dispose();
            _pipeClientOut.Close();
            _pipeClientOut = null;
        }
    }
}
