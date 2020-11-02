using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Exceptions
{
    public class BoardNotFoundException : ArduinoException
    {
        public BoardNotFoundException(String message) : base(message) { }
        public BoardNotFoundException(String message, Exception innerException) : base(message, innerException) { }
    }
}
