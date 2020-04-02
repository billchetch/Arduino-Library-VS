using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Solid.Arduino.Firmata;

namespace Chetch.Arduino.Infrared
{
    public abstract class IRTransmitter : IRDevice
    {
        private bool _enabled = false;
        private int _enablePin; //HIGH output means the transmitter is disabled (as there is no voltage across it)
        private int _transmitPin;
        private ArduinoCommand _repeatCommand = null;

        //if last command is same as current command and time diff (millis) between last command and current command
        //is less than RepeatInterval then use _repeatCommand if it exists.
        protected int RepeatInterval { get; set; } = 200; 
        
        public IRTransmitter(String id, String name, int enablePin, int transmitPin, IRDB db = null) : base(id, name, db)
        {
            Category = DeviceCategory.IR_TRANSMITTER;

            _enablePin = enablePin;
            _transmitPin = transmitPin;
            ConfigurePin(_enablePin, PinMode.DigitalOutput);
            ConfigurePin(_transmitPin, PinMode.PwmOutput);

        }

        public override void ReadDevice()
        {
            base.ReadDevice();
            if(DB != null)
            {
                ClearCommands();
                AddCommands(DB.GetCommands(Name));

                _repeatCommand = GetCommand(REPEAT_COMMAND);
            }
        }

        public void Disable()
        {
            Mgr.SetDigitalPin(_enablePin, true);
            _enabled = false;
        }

        public void Enable()
        {
            Mgr.SetDigitalPin(_enablePin, false);
            _enabled = true;
        }

        override protected void ExecuteCommand(ArduinoCommand command, List<Object> extraArgs = null, bool deep = false)
        {
            if(!_enabled){
                List<ArduinoDevice> devices = Mgr.GetDevicesByPin(_transmitPin);
                foreach (var device in devices)
                {
                    if (device is IRTransmitter && device != this)
                    {
                        ((IRTransmitter)device).Disable();
                    }
                }

                Enable();
            }

            base.ExecuteCommand(command, extraArgs, deep);
        }

        override protected void SendCommand(ArduinoCommand command, List<Object> extraArgs = null)
        {
            var timeDiff = (DateTime.Now.Ticks - LastCommandSentOn) / TimeSpan.TicksPerMillisecond;
            if (_repeatCommand != null && LastCommandSent != null && LastCommandSent.Equals(command) && timeDiff < RepeatInterval)
            {
                base.SendCommand(_repeatCommand, extraArgs);
                LastCommandSent = command;
            }
            else
            {
                base.SendCommand(command, extraArgs);
            }
        }
    }
}
