using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using Chetch.Services;
using Chetch.Utilities;

namespace Chetch.Arduino
{
    abstract public class ArduinoServiceClient<D> : NamedPipeServiceClient<ArduinoServiceMessage, D> where D : ArduinoServiceData, new()
    {
        public ArduinoServiceClient(String serviceInboundID) : base(serviceInboundID)
        {

        }

        ArduinoServiceMessage CreateCommand(String command, String[] args)
        {
            var cmd = new ArduinoServiceMessage();
            cmd.Type = NamedPipeManager.MessageType.COMMAND;
            cmd.Add(command);
            if(args != null)
            {
                cmd.Values.AddRange(args);
            }
            return cmd;
        }

        override protected void HandleReceivedMessage(ArduinoServiceMessage message)
        {
            ServiceData.DeviceManagerStatus = message.DeviceManagerStatus;
            switch (message.Type)
            {
                case NamedPipeManager.MessageType.STATUS_RESPONSE:
                    ServiceData.DeviceCount = message.DeviceCount;
                    break;
            }


            base.HandleReceivedMessage(message);
        }
    }
}
