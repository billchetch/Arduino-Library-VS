using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using Chetch.Services;

namespace Chetch.Arduino
{
    abstract public class ArduinoServiceClient : NamedPipeServiceClient<ArduinoServiceMessage>
    {
        public ADMStatus DeviceManagerStatus { get { return _admStatus; } }

        private ADMStatus _admStatus; 
        
        public ArduinoServiceClient(String serviceInboundID) : base(serviceInboundID)
        {

        }

        override protected void HandleReceivedMessage(ArduinoServiceMessage message)
        {
            _admStatus = message.DeviceManagerStatus;

            base.HandleReceivedMessage(message);
        }
    }
}
