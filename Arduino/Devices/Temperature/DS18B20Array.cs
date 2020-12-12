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
            public bool Enabled { get; set; } = true;
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

        public const int ERROR_TOO_MUCH_NOISE = 85;
        public const int ERROR_NO_READING = -127;
        public const String COMMAND_READ_TEMP = "read-temp";
        public const String PARAM_SENSOR_COUNT = "SensorCount";
        public const String PARAM_ONE_WIRE_PIN = "OneWirePin";
        public const String PARAM_TEMPERATURE = "Temperature";

        private int _oneWirePin;

        public int Resolution { get; set; } = 9;
        public List<DS18B20Sensor> Sensors { get; } = new List<DS18B20Sensor>();
        
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

            message.AddArgument(_oneWirePin);
            message.AddArgument(Resolution);
        }

        protected override void OnConnect(ADMMessage message)
        {
            if (message.Arguments.Count > 0)
            {
                int sc = System.Math.Min(Sensors.Count, message.ArgumentAsInt(0));
                for (int i = 0; i < sc; i++)
                {
                    DS18B20Array.DS18B20Sensor sensor = Sensors[i];
                    if (SampleInterval > 0 && SampleSize > 0 && !sensor.IsConnected)
                    {
                        Mgr.Sampler.Add(sensor, SampleInterval, SampleSize, SamplingOptions);
                        sensor.IsConnected = true;
                    }
                }
                message.AddValue(PARAM_SENSOR_COUNT, ConnectedSensors.Count);
                message.AddValue(PARAM_ONE_WIRE_PIN, message.ArgumentAsInt(1));
            }
        }


        public override void Disconnect()
        {
            base.Disconnect();
            if (Mgr.Sampler != null)
            {
                foreach (var sensor in Sensors)
                {
                    Mgr.Sampler.Remove(sensor);
                }
            }
        }

        public override void HandleMessage(ADMMessage message)
        {
            if (message.Type == Messaging.MessageType.DATA)
            {
                int sc = System.Math.Min(Sensors.Count, message.ArgumentAsInt(0));
                for (int i = 0; i < sc; i++)
                {
                    float temp = message.ArgumentAsFloat(i + 1);
                    bool error = false;
                    String errMsg = null;
                    switch ((int)temp)
                    {
                        case ERROR_TOO_MUCH_NOISE:
                            error = true;
                            errMsg = "Too much noise";
                            break;

                        case ERROR_NO_READING:
                            error = true;
                            errMsg = "No reading";
                            break;

                        default:
                            break;
                    }

                    if (!error) { 
                        Sensors[i].SetTemperature(temp);

                        //prettyify the message
                        message.AddValue(PARAM_TEMPERATURE + "-" + i, temp);
                    } else
                    {
                        //exception will be processed and turned to an Error message
                        throw new Exception(String.Format("Temperature of {0} indicates error: {1}", temp, errMsg));
                    }
                }
                message.AddValue(PARAM_SENSOR_COUNT, sc);
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
