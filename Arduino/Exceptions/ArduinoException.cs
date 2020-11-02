using Chetch.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Exceptions
{
    public class ArduinoException : Exception
    {
        public ArduinoException(String message) : base(message){}
        public ArduinoException(String message, Exception innerException) : base(message, innerException){}
    }
}
