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

    public enum DeviceCategory
    {
        NOT_SET,
        DIAGNOSTICS,
        IR_TRANSMITTER,
        IR_RECEIVER
    }

    public class ArduinoCommand
    {
        public enum CommandType
        {
            NOT_SET,
            SEND,
            RESET,
            INITIALISE,
            RECEIVE,
            START,
            STOP,
            OPEN,
            CLOSE,
            RECORD
        }

        public String CommandAlias { get; set; }
        public CommandType Type { get; set; } = CommandType.NOT_SET; //request to perform a certain command e.g. Send or Delete or Reset etc. etc.
        public List<Object> Arguments { get; set; } = new List<Object>();
        public List<ArduinoCommand> Commands { get; set; } = new List<ArduinoCommand>();
        public uint Repeat { get; set; } = 1;

        public ArduinoCommand()
        {

        }

        public ArduinoCommand(String commandAlias, uint repeat  = 1)
        {
            CommandAlias = commandAlias;
            Repeat = repeat;
        }

        public void AddArgument(Object arg)
        {
            Arguments.Add(arg);
        }
    }

    public class ArduinoDevice
    {
        public String ID { get; internal set; }
        public byte BoardID { get; set; } //ID of 'device' on the arduino board ... used by code on the board to determine what should process the command 
        public String Name { get; set; }
        public List<ArduinoPin> Pins { get; internal set; }
        public DeviceCategory Category { get; set; } = DeviceCategory.NOT_SET;

        private Dictionary<String, ArduinoCommand> _commands = new Dictionary<string, ArduinoCommand>();

        public ArduinoDeviceManager Mgr { get; set; }

        public ArduinoDevice()
        {
            //Empty constructor
        }

        public ArduinoDevice(String name)
        {
            Name = name;
        }

        public ArduinoDevice(String id, String name, byte boardID = 0)
        {
            ID = id;
            Name = name;
            BoardID = boardID;
        }

        public bool IsPinCompatible(ArduinoPin pin)
        {
            if (Pins == null || pin.Mode == PinMode.Undefined) return true;

            //Check that it doesn't conflict with existing pins
            foreach (var p in Pins)
            {
                if (p.PinNumber == pin.PinNumber)
                {
                    return (p.Mode == PinMode.Undefined) || (p.Mode == pin.Mode);
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

        public ArduinoCommand AddCommand(ArduinoCommand command)
        {
            var key = command.CommandAlias.ToLower();
            if (_commands.ContainsKey(key))
            {
                throw new Exception("Already contains a command with alias " + command.CommandAlias);
            }
            _commands[key] = command;
            return command;
        }

        public ArduinoCommand AddCommand(String commandAlias,  uint repeat = 1)
        {
            return AddCommand(new ArduinoCommand(commandAlias, repeat));
        }

        public ArduinoCommand AddCommand(String commandAlias, String[] commandAliases, uint repeat = 1)
        {
            var command = new ArduinoCommand(commandAlias, repeat);
            for(int i = 0; i < commandAliases.Length; i++)
            {
                var c = GetCommand(commandAliases[i]);
                if (c == null) throw new Exception("No command found with alias " + commandAliases[i]);
                command.Commands.Add(c);
            }
            return AddCommand(command);
        }

        virtual public void AddCommands(List<ArduinoCommand> commands)
        {
            foreach(var command in commands)
            {
                AddCommand(command);
            }
        }

        virtual public void ExecuteCommand(String commandAlias)
        {
            var command = GetCommand(commandAlias);
            if (command == null) throw new Exception("Command with alias " + commandAlias + " does not exist");
            ExecuteCommand(command);
        }

        virtual protected void ExecuteCommand(ArduinoCommand command)
        {
            for(int i = 0; i < command.Repeat; i++)
            {
                if(command.Commands.Count > 0)
                {
                    foreach(var ccommand in command.Commands)
                    {
                        ExecuteCommand(ccommand);
                    }
                } else
                {
                    if (Mgr == null) throw new Exception("Device has not yet been added to a device manager");

                    Mgr.SendCommand(BoardID, command);
                    //System.Diagnostics.Debug.Print(command.CommandAlias);
                }
            }
        }
    }
}
