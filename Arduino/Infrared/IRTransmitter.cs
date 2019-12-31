using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Solid.Arduino.Firmata;

namespace Chetch.Arduino.Infrared
{
    public abstract class IRTransmitter : ArduinoDevice
    {
        private int _enablePin; //HIGH output means the transmitter is disabled (as there is no voltage across it)
        private int _transmitPin;
        private Dictionary<String, String> _commands = new Dictionary<string, string>();

        public IRTransmitter(String name, int enablePin, int transmitPin, IRDB db = null) : base(name)
        {
            _enablePin = enablePin;
            _transmitPin = transmitPin;
            ConfigurePin(_enablePin, PinMode.DigitalOutput);
            ConfigurePin(_transmitPin, PinMode.PwmOutput);

            if (db != null) AddCommands(db);
        }

        public void AddCommands(IRDB db)
        {
            _commands.Clear();
            _commands = db.GetCommands(Name);
            if (_commands.Count == 0) throw new Exception("No commands found for " + Name);
        }

        public String GetCommand(String command)
        {
            return "IR " + _commands[command];
        }

        public void Disable()
        {
            mgr.SetDigitalPin(_enablePin, true);
        }

        public void Enable()
        {
            mgr.SetDigitalPin(_enablePin, false);
        }

        public void SendCommand(String command)
        {
            if (mgr == null) throw new Exception("Device has not yet been added to a device manager");

            List<ArduinoDevice> devices = mgr.GetDevicesByPin(_transmitPin);
            foreach (var device in devices)
            {
                if (device is IRTransmitter && device != this)
                {
                    ((IRTransmitter)device).Disable();
                }
            }

            Enable();

            mgr.SendString(GetCommand(command));
        }

        public void SendCommands(String commands, int delayInMillis = 100)
        {
            var cmds = commands.Split(',');
            SendCommand(cmds[0]);
            for (int i = 1; i < cmds.Length; i++)
            {
                if (delayInMillis > 0) System.Threading.Thread.Sleep(delayInMillis);
                mgr.SendString(GetCommand(cmds[i]));
            }
        }
    }
}
