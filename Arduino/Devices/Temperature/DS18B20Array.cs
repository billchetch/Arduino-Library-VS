using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Utilities;

namespace Chetch.Arduino.Devices.Temperature
{
    public class DS18B20Array : TemperatureSensorBase
    {
        protected class DS18B20Sensor : ISampleSubject
        {
            Sampler _sampler;
            public double Temperature { get; set; }

            public DS18B20Sensor()
            {
            }

            public void RequestSample(Sampler sampler)
            {
                _sampler = sampler;
            }

            public void SetTemperature(double temp)
            {
                Temperature = temp;
                _sampler?.ProvideSample(this, temp);
            }
        }

        public const String COMMAND_READ_TEMP = "read-temp";

        private int _oneWirePin;
        protected List<DS18B20Sensor> Sensors = new List<DS18B20Sensor>();
        
        public DS18B20Array(int oneWirePin, String id) : base(id, "DS18B20")
        {
            _oneWirePin = oneWirePin;

            MeasurementUnit = Measurement.Unit.CELSIUS;

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

        public override void HandleMessage(ADMMessage message)
        {
            if (message.Type == Messaging.MessageType.CONFIGURE_RESPONSE && message.HasValue("SensorCount") && Sensors.Count == 0)
            {
                int sc = message.GetInt("SensorCount");
                for (int i = 0; i < sc; i++)
                {
                    DS18B20Sensor sensor = new DS18B20Sensor();
                    Sensors.Add(sensor);
                    if (SampleInterval > 0 && SampleSize > 0)
                    {
                        Sampler.Add(sensor, SampleInterval, SampleSize, SamplingOptions);
                    }
                }
            }

            if (message.Type == Messaging.MessageType.DATA)
            {
                for (int i = 0; i < Sensors.Count; i++)
                {
                    String key = "Temperature-" + i;
                    if (message.HasValue(key))
                    {
                        Sensors[i].SetTemperature(message.GetDouble(key));
                    }
                }
            }

            base.HandleMessage(message);
        }

        public override void RequestSample(Sampler sampler)
        {
            ExecuteCommand(COMMAND_READ_TEMP);

            base.RequestSample(sampler);
        }
    } //end class
}
