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

        public override void AddCommands(List<ArduinoCommand> commands)
        {
            base.AddCommands(commands);

            if (commands.Count > 0)
            {
                TryAddCommand("Unmute", "Volume_Up,Volume_Down", 500);
                TryAddCommand("Mute", "Unmute,Mute/Unmute", 500);

                TryAddCommand("TestChannelSet", "1,1", 500);
                TryAddCommand("TestVolumeUp", "Volume_Up", 500, 4);
                TryAddCommand("TestVolumeDown", "Volume_Down", 500, 4);
                TryAddCommand("TestChannelUp", "Channel_Up", 500, 4);
                TryAddCommand("TestChannelDown", "Channel_Down", 500, 4);
                TryAddCommand("TestMain", "TestChannelSet,TestVolumeUp,TestChannelDown,TestVolumeDown,TestChannelUp", 5000);
                TryAddCommand("Test", "On/Off,TestMain,On/Off", 10000);
            }
        }
    }
}
