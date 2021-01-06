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
        public String Source { get; internal set; }
        public ArduinoException(String source, String message) : base(message)
        {
            Source = source;
        }

        public ArduinoException(String message) : base(message){}
        public ArduinoException(String message, Exception innerException) : base(message, innerException){}

        public ArduinoException(String source, String message, Exception innerException) : base(message, innerException)
        {
            Source = source;
        }
    }
}
