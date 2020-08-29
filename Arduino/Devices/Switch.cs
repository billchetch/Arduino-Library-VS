using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Solid.Arduino.Firmata;

namespace Chetch.Arduino.Devices
{
    public class Switch : ArduinoDevice
    {
        public bool IsON { get { return State; } }
        public bool IsOff { get { return !State; } }

        public bool State { get; internal set; } = false;

        private int _pin = 0;

        public Switch(int pin, String id, String name) : base(id, name)
        {
            _pin = pin;
            ConfigurePin(_pin, PinMode.DigitalOutput, State);
        }

        public Switch(int pin) : this(pin, "switch" + pin, "SWITCH") { }

        public void On()
        {
            State = true;
            Mgr.SetDigitalPin(_pin, State);
        }

        public void Off()
        {
            State = false;
            Mgr.SetDigitalPin(_pin, State);
        }
    }
}
