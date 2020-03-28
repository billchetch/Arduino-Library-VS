using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Solid.Arduino.Firmata;

namespace Chetch.Arduino.Infrared
{
    public class IRReceiver : ArduinoDevice
    {
        private bool _receiving = false;
        private int _receivePin;
        private IRDB _db;
        
        public IRReceiver(String id, String name, int receivePin, IRDB db = null) : base(id, name)
        {
            Category = DeviceCategory.IR_RECEIVER;

            _receivePin = receivePin;
            _db = db;

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

        override protected void ExecuteCommand(ArduinoCommand command)
        {
            switch (command.Type)
            {
                case ArduinoCommand.CommandType.START:
                    _receiving = true; break;
                case ArduinoCommand.CommandType.STOP:
                    _receiving = false; break;
            }
            base.ExecuteCommand(command);
        }
    }
}
