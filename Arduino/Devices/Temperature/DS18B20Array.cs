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
        public class DS18B20Sensor : ISampleSubject
        {
            public Sampler Sampler { get; internal set; }
            public String ID { get; set; }
            public double Temperature { get; set; }
            public bool IsConnected { get; set; } = false;

            public double AverageTemperature {
                get
                {
                    return !IsConnected || Sampler == null ? 0 : Sampler.GetAverage(this);
                }
            }

            public DS18B20Sensor(String id)
            {
                ID = id;
            }

            public void RequestSample(Sampler sampler)
            {
                Sampler = sampler;
            }

            public void SetTemperature(double temp)
            {
                Temperature = temp;
                Sampler?.ProvideSample(this, temp);
            }
        }

        public const String COMMAND_READ_TEMP = "read-temp";
        public const String PARAM_SENSOR_COUNT = "SensorCount";
        public const String PARAM_ONE_WIRE_PIN = "OneWirePin";
        public const String PARAM_TEMPERATURE = "Temperature";

        private int _oneWirePin;
        public List<DS18B20Sensor> Sensors { get; } = new List<DS18B20Sensor>();
        public int ActiveSensors { get; internal set; } = 0; //set by the board once device is connected

        public List<DS18B20Sensor> ConnectedSensors { 
            get
            {
                List<DS18B20Sensor> connectedSensors = new List<DS18B20Sensor>();
                foreach (var sensor in Sensors)
                {
                    if (sensor.IsConnected) connectedSensors.Add(sensor);
                }
                return connectedSensors;
            } 
        }
        
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

        public void AddSensor(String sensorID)
        {
            if(GetSensor(sensorID) == null)
            {
                DS18B20Sensor sensor = new DS18B20Sensor(sensorID);
                Sensors.Add(sensor);
            }
        }

        public DS18B20Array.DS18B20Sensor GetSensor(String sensorID)
        {
            foreach(var sensor in Sensors)
            {
                if (sensor.ID.Equals(sensorID)) return sensor;
            }
            return null;
        }

        public override void AddConfig(ADMMessage message)
        {
            base.AddConfig(message);

            //this should be argument with index 3 (argument values are retrieved by numerical index on the arduino)
            message.AddArgument(_oneWirePin);
        }

        protected override void OnConnect(ADMMessage message)
        {
            if (message.Arguments.Count > 0)
            {
                ActiveSensors = System.Math.Min(Sensors.Count, message.ArgumentAsInt(0));
                for (int i = 0; i < ActiveSensors; i++)
                {
                    DS18B20Array.DS18B20Sensor sensor = Sensors[i];
                    if (SampleInterval > 0 && SampleSize > 0 && !sensor.IsConnected)
                    {
                        Mgr.Sampler.Add(sensor, SampleInterval, SampleSize, SamplingOptions);
                        sensor.IsConnected = true;
                    }
                }
                message.AddValue(PARAM_SENSOR_COUNT, ActiveSensors);
            }
        }

        public override void HandleMessage(ADMMessage message)
        {
            if (message.Type == Messaging.MessageType.DATA)
            {
                ActiveSensors = System.Math.Min(ActiveSensors, message.Arguments.Count);
                for (int i = 0; i < ActiveSensors; i++)
                {
                    float temp = message.ArgumentAsFloat(i);
                    Sensors[i].SetTemperature(temp);

                    //prettyify the message
                    message.AddValue(PARAM_TEMPERATURE + "-" + i, temp);
                }
                message.AddValue(PARAM_SENSOR_COUNT, ActiveSensors);
            }

            base.HandleMessage(message);
        }

        public override void RequestSample(Sampler sampler)
        {
            ExecuteCommand(COMMAND_READ_TEMP);

            base.RequestSample(sampler);
        }

        public double GetTemperature(int idx = 0)
        {
            if (Sensors.Count <= idx) throw new ArgumentException(String.Format("Index {0} is not valid", idx));

            return Sensors[idx].Temperature;
        }

        public double GetAverageTemperature(int idx = 0)
        {
            if (Sensors.Count <= idx) throw new ArgumentException(String.Format("Index {0} is not valid", idx));

            return Sensors[idx].AverageTemperature;
        }
    } //end class
}
