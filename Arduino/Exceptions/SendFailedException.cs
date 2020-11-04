using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Exceptions
{
    public class SendFailedException : ArduinoException
    {
        public SendFailedException(String message) : base(message) { }
        public SendFailedException(String message, Exception innerException) : base(message, innerException) { }
    }
}
