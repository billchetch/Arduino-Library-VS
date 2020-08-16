using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Devices.Temperature
{
    abstract public class TemperatureSensorBase : ArduinoDevice
    {
        public TemperatureSensorBase(String id, String name) : base(id, name)
        {
            Category = DeviceCategory.TEMPERATURE_SENSOR;
        }
    }
}
