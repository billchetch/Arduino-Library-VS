using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Infrared
{
    public class IRSamsungTV : IRTransmitter
    {
        const String NAME = "Samsung TV";

        public IRSamsungTV(int enablePin, int transmitPin, IRDB db) : base(NAME, enablePin, transmitPin, db)
        {

        }
    }
}
