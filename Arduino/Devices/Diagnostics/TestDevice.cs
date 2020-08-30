using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Devices.Diagnostics
{
    public class TestDevice : DiagnosticsDevice
    {
        public TestDevice(String id, String name) : base(id, name)
        {
            TryAddCommand("test", ArduinoCommand.CommandType.TEST, true);
        }

        public TestDevice() : this("test", "TEST") { }
    }
}
