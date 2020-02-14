using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Diagnostics
{
    public class LEDBuiltIn : ArduinoDevice
    {
        public const string LED_BUILTIN_ID = "LEDBI";

        private int _ledBuiltInPinNumber = 13;

        public LEDBuiltIn(int pin) : base(LED_BUILTIN_ID, LED_BUILTIN_ID)
        {
            _ledBuiltInPinNumber = pin;
        }
    }
}
