using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Solid.Arduino.Firmata;
using Chetch.Utilities;

namespace Chetch.Arduino.Infrared
{
    public abstract class IRReceiver : IRDevice
    {
        private bool _receiving = false;
        private String _commandName;
        private int _receivePin;
        private Dictionary<String, IRCode> _irCodes = new Dictionary<String, IRCode>();
        private Dictionary<long, IRCode> _unknownCodes = new Dictionary<long, IRCode>();
        private List<long> _ignoreCodes = new List<long>(); //codes we ignore

        public Dictionary<String, IRCode> IRCodes
        {
            get { return _irCodes;  }
        }
        public Dictionary<long, IRCode> UnknownIRCodes
        {
            get { return _unknownCodes; }
        }

        public IRReceiver(String id, String name, int receivePin, IRDB db = null) : base(id, name, db)
        {
            Category = DeviceCategory.IR_RECEIVER;

            _receivePin = receivePin;
            
            ConfigurePin(_receivePin, PinMode.DigitalInput);

            ArduinoCommand cmd = new ArduinoCommand();
            cmd.CommandAlias = "Start";
            cmd.Type = ArduinoCommand.CommandType.START;
            AddCommand(cmd);

            cmd = new ArduinoCommand();
            cmd.CommandAlias = "Stop";
            cmd.Type = ArduinoCommand.CommandType.STOP;
            AddCommand(cmd);
        }

        override public void ReadDevice()
        {
            base.ReadDevice();

            ArduinoCommand cmd = DB.GetCommand(Name, REPEAT_COMMAND);
            if (cmd != null)
            {
                long code = System.Convert.ToInt64(cmd.Arguments[0]);
                _ignoreCodes.Add(code);
            }
        }

        override protected void ExecuteCommand(ArduinoCommand command, List<Object> extraArgs = null, bool deep = false)
        {
            switch (command.Type)
            {
                case ArduinoCommand.CommandType.START:
                    _receiving = true;
                    if(extraArgs != null && extraArgs.Count > 0)
                    {
                        _commandName = (String)extraArgs[0];
                    }

                    break;
                case ArduinoCommand.CommandType.STOP:
                    _receiving = false;
                    break;
            }
            base.ExecuteCommand(command, extraArgs, deep);
        }

        virtual public void processCode(long code, int protocol, int bits = 32)
        {
            processCode(_commandName, code, protocol, bits);
        }

        virtual public void processCode(String commandName, long code, int protocol, int bits)
        {
            if (commandName == null || commandName.Length == 0 || _ignoreCodes.Contains(code)) return;

            IRCode irc = new IRCode();
            if (_irCodes.ContainsKey(commandName))
            {
                if(_irCodes[commandName].code != code)
                {
                    irc.code = code;
                    irc.protocol = protocol;
                    irc.bits = bits;
                    _unknownCodes[code] = irc;
                }
            } else
            {
                irc.code = code;
                irc.protocol = protocol;
                irc.bits = bits;
                _irCodes[commandName] = irc;
            }   
        }

        virtual public void processUnknownCode(String commandName, IRCode irc)
        {
            if (commandName == null || commandName.Length == 0) return;

            if (!_irCodes.ContainsKey(commandName))
            {
                _irCodes[commandName] = irc;
            } else
            {
                throw new Exception(commandName + " is not unknown");
            }
        }

        public override void HandleMessage(ADMMessage message)
        {
            base.HandleMessage(message);
            if (!IsConnected) return;

            switch (message.Type)
            {
                case NamedPipeManager.MessageType.DATA:
                    if(message.HasValues("Code","Protocol","Bits"))
                    {
                        long ircode = message.GetLong("Code");
                        int protocol = message.GetInt("Protocol");
                        int bits = message.GetInt("Bits");

                        processCode(ircode, protocol, bits);
                    }
                    break;
            }
        }

        public void WriteIRCodes()
        {
            if (DB == null) throw new Exception("No database available");
            WriteDevice();
            if (DBID == 0) throw new Exception("No database ID value for device");

            var commandAliases = Chetch.Database.IDMap<String>.Create(DB.SelectCommandAliases(), "command_alias");
            foreach (var kv in _irCodes)
            {
                IRCode irc = kv.Value;

                long caid;
                if (!commandAliases.ContainsKey(kv.Key))
                {
                    caid = DB.InsertCommandAlias(kv.Key);
                }
                else
                {
                    caid = System.Convert.ToInt64(commandAliases[kv.Key]["id"]);
                }

                try { 
                    DB.InsertCommand(DBID, caid, irc.code, irc.protocol, irc.bits);
                } catch (Exception e)
                {
                    //can happen if ir code is a duplicate
                    //Console.WriteLine(e.Message);
                }
            }
        }
    }
}
