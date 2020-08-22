using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Solid.Arduino.Firmata;
using Chetch.Utilities;

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
        IR_RECEIVER,
        TEMPERATURE_SENSOR,
        COUNTER,
        RANGE_FINDER
    }

    public enum FilterCommandsOptions
    {
        ALL,
        BASE_ONLY,
        COMPOUND_ONLY,
        BY_TYPE
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
            RECORD,
            SAVE,
            READ
        }

        public String CommandAlias { get; set; }
        public CommandType Type { get; set; } = CommandType.NOT_SET; //request to perform a certain command e.g. Send or Delete or Reset etc. etc.
        public List<Object> Arguments { get; set; } = new List<Object>();
        public List<ArduinoCommand> Commands { get; set; } = new List<ArduinoCommand>();
        public int Delay { get; set; } = 0; //delay in milliseconds between 'child' commands
        public int Repeat { get; set; } = 1;
        public int MinWaitTime
        {
            get
            {
                return Delay * Repeat;
            }
        }
        public bool IsCompound
        {
            get
            {
                return Commands.Count > 0;
            }
        }


        public ArduinoCommand()
        {

        }

        public ArduinoCommand(String commandAlias)
        {
            CommandAlias = commandAlias;
        }

        public ArduinoCommand(String commandAlias, CommandType commandType)
        {
            CommandAlias = commandAlias;
            Type = commandType;
        }

        public void AddArgument(Object arg)
        {
            Arguments.Add(arg);
        }

        public override bool Equals(object obj)
        {
            if (obj == null) return false;
            if (!(obj is ArduinoCommand)) return false;

            var cmd = (ArduinoCommand)obj;
            if (!CommandAlias.Equals(cmd.CommandAlias, StringComparison.OrdinalIgnoreCase)) return false;

            if (Commands.Count != cmd.Commands.Count) return false;
            for(int i = 0; i < Commands.Count; i++)
            {
                if (!Commands[i].Equals(cmd.Commands[i])) return false;
            }

            if (Arguments.Count != cmd.Arguments.Count) return false;
            for(int i = 0; i < Arguments.Count; i++)
            {
                if (!Arguments[i].Equals(cmd.Arguments[i])) return false;
            }
            return true;
        }

        public override string ToString()
        {
            String s;
            if(IsCompound)
            {
                s = "";
                foreach(var cmd in Commands)
                {
                    s += (s.Length > 0 ? ", " : "") + cmd.CommandAlias;
                }
                s = CommandAlias + ": " + s;
            }
            else
            {
                s = CommandAlias + ": " + String.Join(", ",  Arguments);
            }
            return s;
        }
    }

    public class ArduinoDevice : ISampleSubject
    {
        private const int MAX_ID_LENGTH = 8;
        private const int MAX_NAME_LENGTH = 10;

        public String ID { get; internal set; }
        public byte BoardID { get; set; } //ID of 'device' on the arduino board ... used by code on the board to determine what should process the command 
        virtual public String Name { get; set; }
        public List<ArduinoPin> Pins { get; internal set; }
        public DeviceCategory Category { get; set; } = DeviceCategory.NOT_SET;
        public bool IsConnected { get; internal set; } = false;

        private Dictionary<String, ArduinoCommand> _commands = new Dictionary<string, ArduinoCommand>();
        public ArduinoCommand LastCommandSent { get; internal set; } = null;
        public long LastCommandSentOn { get; internal set; } = 0;

        public ArduinoDeviceManager Mgr { get; set; }
        protected Sampler Sampler { get; set; }
        public Measurement.Unit MeasurementUnit { get; set; } = Measurement.Unit.NONE;

        public double SampledAverage { get { return Sampler == null ? 0 : Sampler.GetAverage(this) ;  } }
        public ArduinoDevice()
        { 
            //Empty constructor
        }

        public ArduinoDevice(String name) : this(null, name)
        {
            
        }

        public ArduinoDevice(String id, String name, byte boardID = 0)
        {
            if(id != null && id.Length > MAX_ID_LENGTH)
            {
                throw new ArgumentException(String.Format("{0} exceeds the maximum number {1} characters for an ID", id, MAX_ID_LENGTH));
            }
            if (name != null && name.Length > MAX_NAME_LENGTH)
            {
                throw new ArgumentException(String.Format("{0} exceeds the maximum number {1} characters for an ID", name, MAX_NAME_LENGTH));
            }
            ID = id;
            Name = name;
            BoardID = boardID;
        }

        override public String ToString()
        {
            return String.Format("{0} {1} {2} {3}", ID, Name, Category.ToString(), IsConnected ? "Connected" : "Not connected");
        }

        public bool IsUsingPin(int pinNumber)
        {
            if(Pins != null)
            { 
                foreach(var p in Pins)
                {
                    if(p.PinNumber == pinNumber)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public bool IsPinCompatible(ArduinoPin pin)
        {
            return IsPinCompatible(pin.PinNumber, pin.Mode);
        }

        public bool IsPinCompatible(int pinNumber, PinMode pinMode)
        {
            if (Pins == null || pinMode == PinMode.Undefined) return true;

            //Check that it doesn't conflict with existing pins
            foreach (var p in Pins)
            {
                if (p.PinNumber == pinNumber)
                {
                    return (p.Mode == PinMode.Undefined) || (p.Mode == pinMode);
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

        public bool HasCommand(String commandAlias)
        {
            return GetCommand(commandAlias) != null;
        }

        public List<ArduinoCommand> GetCommands(FilterCommandsOptions options = FilterCommandsOptions.ALL)
        {
            List<ArduinoCommand> commands = new List<ArduinoCommand>();

            if (_commands == null || _commands.Count == 0) return commands;

            commands = _commands.Values.ToList();
            switch (options)
            {
                case FilterCommandsOptions.BASE_ONLY:
                    commands = commands.Where<ArduinoCommand>(x => !x.IsCompound).ToList();
                    break;
                case FilterCommandsOptions.COMPOUND_ONLY:
                    commands = commands.Where<ArduinoCommand>(x => x.IsCompound).ToList();
                    break;
                case FilterCommandsOptions.BY_TYPE:
                    throw new NotImplementedException("Not yet implemented");
                    break;

            }
            return commands;
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

        public ArduinoCommand AddCommand(String commandAlias, ArduinoCommand.CommandType commandType = ArduinoCommand.CommandType.NOT_SET)
        {
            return AddCommand(new ArduinoCommand(commandAlias, commandType));
        }

        public ArduinoCommand AddCommand(String commandAlias, String[] commandAliases, int delay = 1, int repeat = 1)
        {
            var command = new ArduinoCommand(commandAlias);
            command.Delay = delay;
            command.Repeat = repeat;
            if(commandAliases != null)
            { 
                for(int i = 0; i < commandAliases.Length; i++)
                {
                    var c = GetCommand(commandAliases[i]);
                    if (c == null) throw new Exception("No command found with alias " + commandAliases[i]);
                    command.Commands.Add(c);
                }
            }
            return AddCommand(command);
        }

        public ArduinoCommand TryAddCommand(String commandAlias, String commandAliases, int delay = 1, int repeat = 1)
        {
            try
            {
                return AddCommand(commandAlias, commandAliases != null ? commandAliases.Split(',') : null, delay, repeat);
            } catch (Exception)
            {
                return null;
            }
        }

        public ArduinoCommand TryAddCommand(String commandAlias, ArduinoCommand.CommandType commandType = ArduinoCommand.CommandType.NOT_SET)
        {
            ArduinoCommand cmd = TryAddCommand(commandAlias, null);
            if(cmd != null)cmd.Type = commandType;
            return cmd;
        }

        virtual public void AddCommands(List<ArduinoCommand> commands)
        {
            foreach(var command in commands)
            {
                AddCommand(command);
            }
        }

        virtual public void ClearCommands(FilterCommandsOptions options = FilterCommandsOptions.ALL)
        {
            if (options == FilterCommandsOptions.ALL)
            {
                _commands.Clear();
            } else
            {
                List<ArduinoCommand> commands2remove = GetCommands(options);
                foreach(var cmd in commands2remove)
                {
                    _commands.Remove(cmd.CommandAlias);
                }
            }
        }

        virtual public void ExecuteCommand(String commandAlias, List<Object> extraArgs = null)
        {
            var command = GetCommand(commandAlias);
            if (command == null) throw new Exception("Command with alias " + commandAlias + " does not exist");
            ExecuteCommand(command, extraArgs, false);
        }

        virtual protected void ExecuteCommand(ArduinoCommand command, List<Object> extraArgs = null, bool deep = false)
        {
            if(command.Commands.Count > 0)
            {
                for (int i = 0; i < command.Repeat; i++)
                {
                    foreach (var ccommand in command.Commands)
                    {
                        ExecuteCommand(ccommand, deep ? extraArgs : null, deep);
                        if (command.Delay > 0)
                        {
                            System.Threading.Thread.Sleep(command.Delay);
                        }
                    }
                } //end command repeat
            } else
            {
                SendCommand(command, extraArgs);
            }
        }

        virtual protected void SendCommand(ArduinoCommand command, List<Object> extraArgs = null)
        {
            if (Mgr == null) throw new Exception(String.Format("Device {0} has not yet been added to a device manager", ToString()));
            if (!IsConnected) throw new Exception(String.Format("Device {0} is not 'connected' to board", ToString()));
            Mgr.SendCommand(BoardID, command, extraArgs);
            LastCommandSent = command;
            LastCommandSentOn = DateTime.Now.Ticks;
        }

        //messaging
        virtual protected void Broadcast(ADMMessage message)
        {
            message.Sender = ID;
            Mgr.Broadcast(message);
        }

        virtual public void HandleMessage(ADMMessage message)
        {
            switch (message.Type)
            {
                case Messaging.MessageType.CONFIGURE_RESPONSE:
                    IsConnected = true;
                    OnConnect(message);
                    break;
                default:
                    break;
            }
        }

        virtual public void HandleDigitalPinStateChange(int pinNumber, bool newState)
        {
            //a hook
        }

        //called after the device has been configured on the board
        virtual protected void OnConnect(ADMMessage message)
        {
            //hook
        }

        virtual public void AddConfig(ADMMessage message)
        {
            //add board ID, device category and device name
            message.TargetID = BoardID;
            message.AddArgument((byte)Category);
            message.AddArgument(Name);
            
#if DEBUG
            System.Diagnostics.Debug.Print(String.Format("Adding config for device {0} ... ", ID));
#endif
        }

        virtual public void RequestSample(Sampler sampler)
        {
            Sampler = sampler;
        }

        public void ConfigureSampler(int interval, int sampleSize, Sampler.SamplingOptions samplingOptions = Sampler.SamplingOptions.MEAN)
        {
            if (Mgr == null) throw new Exception("Cannot configure sampling for device before adding to ADM");
            Mgr.Sampler.Add(this, interval, sampleSize, samplingOptions);
        }
    }
}
