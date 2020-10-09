using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino
{
    public class ArduinoDeviceGroup
    {
        public ArduinoDeviceManager ADM { get; set; }
        public String ID { get; internal set; }
        public String Name { get; internal set; }

        public List<ArduinoDevice> Devices { get; internal set; }

        public ArduinoDeviceGroup(String id, String name)
        {
            ID = id;
            Name = name;
        }

        public void AddDevice(ArduinoDevice dev)
        {
            if (dev == null) return;

            if (!Devices.Contains(dev))
            {
                Devices.Add(dev);
            }
        }
        
        public ArduinoDevice GetDevice(String deviceID)
        {
            foreach(var dev in Devices)
            {
                if (dev.ID != null && dev.ID.Equals(deviceID)) return dev;
            }
            return null;
        }

    } //end class
}
