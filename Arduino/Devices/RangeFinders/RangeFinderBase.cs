using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Devices.RangeFinders
{
    abstract public class RangeFinderBase : ArduinoDevice
    {
        public RangeFinderBase(String id, String name) : base(id, name)
        {
            Category = DeviceCategory.RANGE_FINDER;
        }
    }
}
