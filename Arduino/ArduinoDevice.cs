using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Solid.Arduino.Firmata;

namespace Chetch.Arduino
{
    public struct ArduinoPin
    {
        public int PinNumber { get; set; }
        public PinMode Mode { get; set; }
        public long InitialValue { get; set; }
        public long CurrentValue { get; set; }
    }

    public class ArduinoCommand
    {
        public String CommandAlias { get; set; }
        public String Command { get; set; } = null;
        public List<ArduinoCommand> Commands { get; set; } = new List<ArduinoCommand>();
        public int Repeat { get; set; } = 1;
    }

    public class ArduinoDevice
    {
        public String ID { get; internal set; }
        public String Name { get; internal set; }
        public List<ArduinoPin> Pins { get; internal set; }

        private Dictionary<String, ArduinoCommand> _commands = new Dictionary<string, ArduinoCommand>();

        public ArduinoDeviceManager mgr { get; set; }

        public ArduinoDevice()
        {
            //Empty constructor
        }

        public ArduinoDevice(String name)
        {
            Name = name;
        }

        public ArduinoDevice(String id, String name)
        {
            ID = id;
            Name = name;
        }

        public bool IsPinCompatible(ArduinoPin pin)
        {
            if (Pins == null) return true;

            //Check that it doesn't conflict with existing pins
            foreach (var p in Pins)
            {
                if (p.PinNumber == pin.PinNumber)
                {
                    return (p.Mode == pin.Mode);
                }
            }

            //this device is not even using this pin
            return true;
        }

        protected ArduinoPin ConfigurePin(int pinNumber, PinMode pinMode, long initialValue = -1)
        {
            ArduinoPin pin = new ArduinoPin();
            pin.PinNumber = pinNumber;
            pin.Mode = pinMode;
            pin.InitialValue = initialValue;
            pin.CurrentValue = -1;

            //Check that it doesn't conflict with existing pins
            if (!IsPinCompatible(pin)) throw new Exception("Cannot configure pin as it is not compatible with existing pins");

            if (Pins == null) Pins = new List<ArduinoPin>();
            Pins.Add(pin);

            return pin;
        }

        public void SendCommand(String commandAlias, String[] args = null)
        {
            if (!_commands.ContainsKey(commandAlias)) throw new Exception("Command with alias " + commandAlias + " does not exist");
            SendCommand(_commands[commandAlias], args);
        }

        public void SendCommand(ArduinoCommand command, String[] args = null)
        {
            for(int i = 0; i < command.Repeat; i++)
            {
                if(command.Commands.Count > 0)
                {
                    foreach(var ccommand in command.Commands)
                    {
                        SendCommand(ccommand, args);
                    }
                } else
                {
                    SendCommandString(command.Command, args);
                }
            }
        }

        virtual protected String CreateCommandString(String command, String[] args)
        {
            String argString = "";
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    argString += (i == 0 ? "" : " ") + args[i];
                }
            }
            return command + argString;
        }

        virtual protected void SendCommandString(String command, String[] args)
        {
            if (mgr == null) throw new Exception("Device has not yet been added to a device manager");

            var commandString = CreateCommandString(command, args);
            mgr.SendString(commandString);
        }
        
    }
}
