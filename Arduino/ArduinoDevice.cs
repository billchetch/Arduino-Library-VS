using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Solid.Arduino.Firmata;
using Chetch.Utilities;
using Chetch.Application;

namespace Chetch.Arduino
{
    public struct ArduinoPin
    {
        public const int BOARD_SPECIFIED = -1;

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
            READ,
            TEST
        }

        public String CommandAlias { get; set; }
        public CommandType Type { get; set; } = CommandType.NOT_SET; //request to perform a certain command e.g. Send or Delete or Reset etc. etc.
        public List<Object> Arguments { get; set; } = new List<Object>();
        public List<ArduinoCommand> Commands { get; set; } = new List<ArduinoCommand>();
        public int Delay { get; set; } = 0; //delay in milliseconds between 'child' commands
        public int Repeat { get; set; } = 1;
        public bool ExpectsResponse { get; set; } = false;
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

    public class ExecutionArguments
    {
        public List<Object> Arguments;
        public byte Tag = 0;
        public bool Deep = false;

        public ExecutionArguments(List<Object> args, byte tag = 0, bool deep = false)
        {
            Arguments = args;
            Tag = tag;
            Deep = deep;
        }

        public Object GetValue(int idx)
        {
            return Arguments.Count > idx ? Arguments[idx] : null;
        }

        public int GetInt(int idx, int defaultValue = 0)
        {
            Object v = GetValue(idx);
            return v == null ? defaultValue : System.Convert.ToInt32(v);
        }

        public String GetString(int idx, String defaultValue = null)
        {
            Object v = GetValue(idx);
            return v == null ? defaultValue : v.ToString();
        }
    }

    public class ArduinoDevice : ISampleSubject
    {
        private const int MAX_ID_LENGTH = 8;
        private const int MAX_NAME_LENGTH = 10;

        public String ID { get; internal set; }
        public byte BoardID { get; set; } //ID of 'device' on the arduino board ... used by code on the board to determine what should process the command 
        virtual public String Name { get; set; }
        public List<ArduinoPin> Pins { get; internal set; } = new List<ArduinoPin>();
        public DeviceCategory Category { get; set; } = DeviceCategory.NOT_SET;
        public bool IsConnected { get; internal set; } = false;

        private Dictionary<String, ArduinoCommand> _commands = new Dictionary<string, ArduinoCommand>();
        public ArduinoCommand LastCommandSent { get; internal set; } = null;
        public long LastCommandSentOn { get; internal set; } = 0;

        public ArduinoDeviceManager Mgr { get; set; }
        protected Sampler Sampler { get; set; }
        public int SampleInterval { get; set; } = 0; //in ms
        public int SampleSize { get; set; } = 0;
        public Sampler.SamplingOptions SamplingOptions { get; set; } = Sampler.SamplingOptions.MEAN_COUNT;
        public Measurement.Unit MeasurementUnit { get; set; } = Measurement.Unit.NONE;

        public ArduinoDevice()
        { 
            //Empty constructor
        }

        public ArduinoDevice(String name) : this(null, name) { }

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

        public String ToString(bool listPins)
        {
            if (listPins)
            {
                String s = "";
                foreach(var p in Pins)
                {
                    s += (s.Length > 0 ? ", " : "") + p.PinNumber + ":" + p.Mode;
                }
                return ToString() + " Pins: " + s;
            } else
            {
                return ToString();
            }
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

        protected ArduinoPin ConfigurePin(int pinNumber, PinMode pinMode, bool initialValue)
        {
            return ConfigurePin(pinNumber, pinMode, initialValue ? 1 : 0);
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

        public ArduinoCommand TryAddCommand(String commandAlias, String commandAliases, int delay = 1, int repeat = 1, ArduinoCommand.CommandType commandType = ArduinoCommand.CommandType.NOT_SET, bool expectsResponse = false)
        {
            try
            {
                ArduinoCommand cmd = AddCommand(commandAlias, commandAliases != null ? commandAliases.Split(',') : null, delay, repeat);
                cmd.Type = commandType;
                cmd.ExpectsResponse = expectsResponse;
                return cmd;
            } catch (Exception)
            {
                return null;
            }
        }

        public ArduinoCommand TryAddCommand(String commandAlias, ArduinoCommand.CommandType commandType = ArduinoCommand.CommandType.NOT_SET, bool expectsResponse = false)
        {
            ArduinoCommand cmd = TryAddCommand(commandAlias, null);
            if(cmd != null)cmd.Type = commandType;
            cmd.ExpectsResponse = expectsResponse;
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

        virtual public byte ThreadExecuteCommand(String command, int repeat, int delay, List<Object> args)
        {
            //check has command
            ArduinoCommand acmd = GetCommand(command);
            if (acmd == null)
            {
                throw new Exception(String.Format("Device {0} does not have command {1}", ID, command));
            }

            //pass an empty array rather than null ... safety measure here just for the ThreadExecution Manager
            if (args == null)
            {
                args = new List<Object>();
            }

            byte tag = acmd.ExpectsResponse ? ADMMessage.CreateNewTag() : (byte)0;
            ExecutionArguments xargs = new ExecutionArguments(args, tag);

            //Use ThreadExecutionManager to allow for multi-threading by device 
            int prevSize = ThreadExecutionManager.MaxQueueSize;
            ThreadExecutionManager.MaxQueueSize = acmd.IsCompound ? 1 : 256;
            ThreadExecutionState xs = ThreadExecutionManager.Execute<ExecutionArguments>(ID, repeat, delay, ExecuteCommand, command, xargs);
            ThreadExecutionManager.MaxQueueSize = prevSize;
            if(xs == null)
            {
                ADMMessage.ReleaseTag(tag);
                tag = 0;
            }
            
            return tag;
        }

        public void ExecuteCommand(String command, params Object[] args)
        {
            ExecutionArguments xargs = new ExecutionArguments(new List<Object>(args));
            ExecuteCommand(command, xargs);
        }

        virtual public void ExecuteCommand(String commandAlias, ExecutionArguments xargs)
        {
            var command = GetCommand(commandAlias);
            if (command == null) throw new Exception("Command with alias " + commandAlias + " does not exist");

            ExecuteCommand(command, xargs);
        }

        virtual protected void ExecuteCommand(ArduinoCommand command, ExecutionArguments xargs)
        {
            if(command.Commands.Count > 0)
            {
                for (int i = 0; i < command.Repeat; i++)
                {
                    foreach (var ccommand in command.Commands)
                    {
                        ExecuteCommand(ccommand, xargs != null && xargs.Deep ? xargs : null);
                        if (command.Delay > 0)
                        {
                            System.Threading.Thread.Sleep(command.Delay);
                        }
                    }
                } //end command repeat
            } else
            {
                SendCommand(command, xargs);
            }
        }

        virtual protected void SendCommand(ArduinoCommand command, ExecutionArguments xargs = null)
        {
            if (Mgr == null) throw new Exception(String.Format("Device {0} has not yet been added to a device manager", ToString()));
            if (!IsConnected) throw new Exception(String.Format("Device {0} is not 'connected' to board", ToString()));
            if (xargs == null)
            {
                Mgr.SendCommand(BoardID, command);
            } else
            {
                Mgr.SendCommand(BoardID, command, xargs.Arguments, xargs.Tag);
            }
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

            if (SampleInterval > 0 && SampleSize > 0)
            {
                Mgr.Sampler.Add(this, SampleInterval, SampleSize, SamplingOptions);
#if DEBUG
                System.Diagnostics.Debug.Print(String.Format("Adding to sampler with interval {0} and sample size {1}", SampleInterval, SampleSize));
#endif
            }
        }

        virtual public void RequestSample(Sampler sampler)
        {
            Sampler = sampler;
        }

        virtual public void Disconnect()
        {
            IsConnected = false;
        }
    }
}
