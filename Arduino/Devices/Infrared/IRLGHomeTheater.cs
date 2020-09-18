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
        public const int REPEAT_COMMAND_INDEX = 0;
        public const int REPEAT_INTERVAL = 120;
        
        public IRLGHomeTheater(String id, int enablePin, int transmitPin, IRDB db) : base(id, "LGHT", enablePin, transmitPin, db)
        {
            DeviceName = DEVICE_NAME;
            RepeatInterval = REPEAT_INTERVAL;
        }

        public override void AddCommands(List<ArduinoCommand> commands)
        {
            base.AddCommands(commands);

            AddRawCommand(REPEAT_COMMAND, 0);
            AddCommand("Unmute", new String[] { "Volume_up", "Volume_down" });
            AddCommand("Mute", new String[] { "Unmute", "Mute/Unmute" });
            AddCommand("MultiRepeat", new String[] { REPEAT_COMMAND }, 30, 50);
            AddCommand("TestRepeat", new String[] { "Volume_Up", "MultiRepeat"}, 40);
            AddCommand("VURepeat", new String[] { "Volume_Up" }, 35, 50);
            AddCommand("VDRepeat", new String[] { "Volume_Down" }, 35, 50);
        }

        public override void AddConfig(ADMMessage message)
        {
            base.AddConfig(message);

            message.AddArgument(REPEAT_COMMAND_INDEX); //repeat command index
        }
    }
}
