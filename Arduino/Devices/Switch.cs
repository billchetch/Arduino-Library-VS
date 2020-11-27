﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Solid.Arduino.Firmata;

namespace Chetch.Arduino.Devices
{
    public class Switch : ArduinoDevice
    {
        public bool IsOn { get { return State; } }
        public bool IsOff { get { return !State; } }

        public bool State { get; protected set; } = false;

        private int _pin = 0;

        public Switch(int pin, String id, String name) : base(id, name)
        {
            _pin = pin;
            ConfigurePin(_pin, PinMode.DigitalOutput, State);
        }

        public Switch(int pin) : this(pin, "switch" + pin, "SWITCH") { }

        protected void SetPin(bool state)
        {
            Mgr.SetDigitalPin(_pin, state);
        }

        virtual public void On()
        {
            if(!Enabled)throw new Exception(String.Format("Cannot turn on as switch {0} is not enabled", ID));
            State = true;
            SetPin(State);
        }

        virtual public void Off()
        {
            if (!Enabled) throw new Exception(String.Format("Cannot turn off as switch {0} is not enabled", ID));
            State = false;
            SetPin(State);
        }
    }
}
