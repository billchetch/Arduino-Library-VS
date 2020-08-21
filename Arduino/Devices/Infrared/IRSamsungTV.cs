using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Devices.Infrared
{
    public class IRSamsungTV : IRTransmitter
    {
        const String NAME = "Samsung TV";

        public IRSamsungTV(String id, int enablePin, int transmitPin, IRDB db) : base(id, NAME, enablePin, transmitPin, db)
        {

        }
    }
}
