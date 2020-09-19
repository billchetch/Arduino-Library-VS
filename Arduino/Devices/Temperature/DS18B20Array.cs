using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Devices.Temperature
{
    public class DS18B20Array : TemperatureSensorBase
    {
        public const String COMMAND_READ_TEMP = "read-temp";

        private int _oneWirePin;
        
        public DS18B20Array(int oneWirePin, String id) : base(id, "DS18B20")
        {
            _oneWirePin = oneWirePin;

            //at time of writing (16/08/2020) the firmata support for OneWire was unclear...
            //so the solution is to have OneWire + DallastTemperatures libs installed on the board
            //and send ADM messages of termperature that include the index of the sensor
            //for handling by the computer...
            //Also note we are not using PULLUP mode as on testing this didn't work ... requirement is therefore
            //to use a 4.7K resistor bridging the data and power wires to the device.
            ConfigurePin(_oneWirePin, Solid.Arduino.Firmata.PinMode.DigitalInput);

            TryAddCommand(COMMAND_READ_TEMP, ArduinoCommand.CommandType.READ, true);
        }

        public DS18B20Array(int oneWirePin) : this(oneWirePin, "ds18" + oneWirePin){}

        
        public override void AddConfig(ADMMessage message)
        {
            base.AddConfig(message);

            //this should be argument with index 3 (argument values are retrieved by numerical index on the arduino)
            message.AddArgument(_oneWirePin);
        }
    }
}
