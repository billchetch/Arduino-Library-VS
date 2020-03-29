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
        
        public IRTransmitter(String id, String name, int enablePin, int transmitPin, IRDB db = null) : base(id, name, db)
        {
            Category = DeviceCategory.IR_TRANSMITTER;

            _enablePin = enablePin;
            _transmitPin = transmitPin;
            ConfigurePin(_enablePin, PinMode.DigitalOutput);
            ConfigurePin(_transmitPin, PinMode.PwmOutput);

            if (db != null) AddCommands(db.GetCommands(name));
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

        /*override public void ExecuteCommand(ArduinoCommand command, String[] args = null)
        {
            if (Mgr == null) throw new Exception("Device has not yet been added to a device manager");

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

            base.ExecuteCommand(command, args);
        }*/
    }
}
