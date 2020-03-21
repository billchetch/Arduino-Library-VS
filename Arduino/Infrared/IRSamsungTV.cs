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

        public IRSamsungTV(String id, byte boardID, int enablePin, int transmitPin, IRDB db) : base(id, NAME, boardID, enablePin, transmitPin, db)
        {

        }
    }
}
