using System;
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

            ConfigurePin(_ledBuiltInPinNumber, PinMode.Undefined);
        }

        override public void ExecuteCommand(String commandAlias, List<Object> extraArgs = null, bool deep = false)
        {
            if (commandAlias == null) throw new Exception("Cannot have a null command alias");
            switch (commandAlias.ToLower())
            {
                case "blinktest":
                    Blink(10, 100);
                    break;

                case "blink":
                    int repeat = extraArgs.Count > 0 ? System.Convert.ToInt16(extraArgs[0]) : 1;
                    int delay = extraArgs.Count > 1 ? System.Convert.ToInt16(extraArgs[1]) : 1000;
                    Blink(repeat, delay);
                    break;

                default:
                    base.ExecuteCommand(commandAlias, extraArgs, deep);
                    break;
            }
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
