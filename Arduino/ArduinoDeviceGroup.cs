using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino
{
    public class ArduinoDeviceGroup
    {
        public ArduinoDeviceManager ADM { get; set; };
        public String ID { get; internal set; }
        public String Name { get; internal set; }

        public List<ArduinoDevice> Devices { get; internal set; }
    }
}
