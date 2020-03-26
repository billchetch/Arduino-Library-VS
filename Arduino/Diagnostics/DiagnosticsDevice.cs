using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Diagnostics
{
    public class DiagnosticsDevice : ArduinoDevice
    {
        public DiagnosticsDevice(String id, String name) : base(id, name)
        {
            Category = DeviceCategory.DIAGNOSTICS;
        }
    }

}
