﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Solid.Arduino.Firmata;

namespace Chetch.Arduino.Diagnostics
{
    public class LEDBuiltIn : DiagnosticsDevice
    {
        public const string LED_BUILTIN_ID = "LEDBI";

        private int _ledBuiltInPinNumber;

        public LEDBuiltIn(int pin = 13) : base(LED_BUILTIN_ID, LED_BUILTIN_ID)
        {
            _ledBuiltInPinNumber = pin;

            ConfigurePin(_ledBuiltInPinNumber, PinMode.DigitalOutput);
        }

        public void Blink(int repeat = 1, int delay = 1000)
        {
            for (int i = 0; i < repeat; i++)
            {
                Mgr.SetDigitalPin(_ledBuiltInPinNumber, true);
                System.Threading.Thread.Sleep(delay);
                Mgr.SetDigitalPin(_ledBuiltInPinNumber, false);
            }
        }
    }
}
