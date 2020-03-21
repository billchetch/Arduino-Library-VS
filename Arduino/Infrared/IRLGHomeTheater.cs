using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Infrared
{
    public class IRLGHomeTheater : IRTransmitter
    {
        const String NAME = "LG Home Theater";

        public IRLGHomeTheater(String id, byte boardID, int enablePin, int transmitPin, IRDB db) : base(id, NAME, boardID, enablePin, transmitPin, db)
        {
           
        }

        public override void AddCommands(List<ArduinoCommand> commands)
        {
            base.AddCommands(commands);

            AddCommand("Unmute", new String[] { "Volume_up", "Volume_down" });
            AddCommand("Mute", new String[] { "Unmute", "Mute/Unmute" });
        }
    }
}
