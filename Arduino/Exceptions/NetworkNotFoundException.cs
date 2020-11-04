using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Exceptions
{
    class NetworkNotFoundException : ArduinoException
    {
        public NetworkNotFoundException(String message) : base(message) { }
        public NetworkNotFoundException(String message, Exception innerException) : base(message, innerException) { }
    }
}
