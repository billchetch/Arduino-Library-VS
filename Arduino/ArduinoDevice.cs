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
        public uint Repeat { get; set; } = 1;

        public ArduinoCommand()
        {

        }

        public ArduinoCommand(String commandAlias, String command, uint repeat  = 1)
        {
            CommandAlias = commandAlias;
            Command = command;
            Repeat = repeat;
        }

        public ArduinoCommand(String commandAlias, uint repeat = 1)
        {
            CommandAlias = commandAlias;
            Repeat = repeat;
        }
    }

    public class ArduinoDevice
    {
        public String ID { get; internal set; }
        public String Name { get; set; }
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

        //Commands
        public ArduinoCommand GetCommand(String commandAlias)
        {
            var key = commandAlias.ToLower();
            return _commands.ContainsKey(key) ? _commands[key] : null;
        }

        public void AddCommand(ArduinoCommand command)
        {
            var key = command.CommandAlias.ToLower();
            if (_commands.ContainsKey(key))
            {
                throw new Exception("Already contains a command with alias " + command.CommandAlias);
            }
            _commands[key] = command;
        }

        public void AddCommand(String commandAlias, String command, uint repeat = 1)
        {
            AddCommand(new ArduinoCommand(commandAlias, command, repeat));
        }

        public void AddCommand(String commandAlias, String[] commandAliases, uint repeat = 1)
        {
            var command = new ArduinoCommand(commandAlias, repeat);
            for(int i = 0; i < commandAliases.Length; i++)
            {
                var c = GetCommand(commandAliases[i]);
                if (c == null) throw new Exception("No command found with alias " + commandAliases[i]);
                command.Commands.Add(c);
            }
            AddCommand(command);
        }

        public void AddCommands(List<ArduinoCommand> commands)
        {
            foreach(var command in commands)
            {
                AddCommand(command);
            }
        }

        public void SendCommand(String commandAlias, String[] args = null)
        {
            var command = GetCommand(commandAlias);
            if (command == null) throw new Exception("Command with alias " + commandAlias + " does not exist");
            SendCommand(command, args);
        }

        virtual public void SendCommand(ArduinoCommand command, String[] args = null)
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
                    argString += " " + args[i];
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
