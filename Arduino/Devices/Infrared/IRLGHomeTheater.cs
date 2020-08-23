using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Devices.Infrared
{
    public class IRLGHomeTheater : IRTransmitter
    {
        public const String DEVICE_NAME = "LG Home Theater";

        public IRLGHomeTheater(String id, int enablePin, int transmitPin, IRDB db) : base(id, "LGHT", enablePin, transmitPin, db)
        {
            DeviceName = DEVICE_NAME;
        }

        public override void AddCommands(List<ArduinoCommand> commands)
        {
            base.AddCommands(commands);

            AddCommand("Unmute", new String[] { "Volume_up", "Volume_down" });
            AddCommand("Mute", new String[] { "Unmute", "Mute/Unmute" });
        }
    }
}
