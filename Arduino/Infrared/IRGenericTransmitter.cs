using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Infrared
{
    public class IRGenericTransmitter : IRTransmitter
    {
        override public String Name
        {
            set
            {
                base.Name = value;
                if(DB != null)ReadDevice();
            }
        }

        public IRGenericTransmitter(String id, String name, int enablePin, int transmitPin, IRDB db = null) : base(id, name, enablePin, transmitPin, db)
        {

        }
    }
}
