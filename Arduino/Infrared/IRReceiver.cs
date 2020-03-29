using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Solid.Arduino.Firmata;

namespace Chetch.Arduino.Infrared
{
    public abstract class IRReceiver : IRDevice
    {
        private bool _receiving = false;
        private String _commandName;
        private int _receivePin;
        private Dictionary<String, IRCode> _irCodes = new Dictionary<String, IRCode>();
        private Dictionary<long, IRCode> _unknownCodes = new Dictionary<long, IRCode>();
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
            if(_commandName != null && _commandName.Length > 0)
            {
                IRCode irc;

                if (_irCodes.ContainsKey(_commandName))
                {
                    irc = _irCodes[_commandName];
                    if(irc.code != code)
                    {
                        _unknownCodes[code] = irc;
                    }
                } else
                {
                    irc = new IRCode();
                    irc.code = code;
                    irc.protocol = protocol;
                    irc.bits = bits;
                    _irCodes[_commandName] = irc;
                }
            }   
        }

        public void WriteIRCodes()
        {
            if (DB == null) throw new Exception("No database available");
            //DB.
        }
    }
}
