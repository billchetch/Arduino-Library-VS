using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Messaging;
using Chetch.Utilities;

namespace Chetch.Arduino.Devices.RangeFinders
{
    public class JSN_SR04T : RangeFinderBase
    {
        public const String DEVICE_NAME = "JSN-SR04T"; //This name must be used if the ChetchFirmata code on the Arduino is to work
        public const String PARAM_DISTANCE = "Distance";
        public const String PARAM_UNITS = "Units";

        public const int MIN_DISTANCE = 25;
        public const int MAX_DISTANCE = 600;

        private int _transmitPin;
        private int _receivePin;
        public double SpeedOfSound { get; set; }


        public JSN_SR04T(int transmitPin, int receivePin, String id) : base(id, DEVICE_NAME)
        {
            _transmitPin = transmitPin;
            _receivePin = receivePin;

            ConfigurePin(_transmitPin, Solid.Arduino.Firmata.PinMode.DigitalOutput, 0);
            ConfigurePin(_receivePin, Solid.Arduino.Firmata.PinMode.DigitalInput);

            MeasurementUnit = Measurement.Unit.CM;
            MinDistance = MIN_DISTANCE;
            MaxDistance = MAX_DISTANCE;

            SpeedOfSound = Measurement.GetSpeedOfSound(30.0); //get for tropical environments
        }

        public JSN_SR04T(int transmitPin, int receivePin) : this(transmitPin, receivePin, "sr04t-" + transmitPin) { }

        public override void AddConfig(ADMMessage message)
        {
            base.AddConfig(message);

            //this should be arguments with indices 3 and 4 (argument values are retrieved by numerical index on the arduino)
            message.AddArgument(_transmitPin);
            message.AddArgument(_receivePin);
        }

        public override void HandleMessage(ADMMessage message)
        {
            base.HandleMessage(message);

            if (message.Type == MessageType.DATA)
            {
                double duration = message.ArgumentAsLong(0); //this is in microseconds
                if (duration > 0)
                {
                    double speedInMicros = Measurement.ConvertUnit(SpeedOfSound, Measurement.Unit.MICROSECOND, Measurement.Unit.SECOND);
                    Distance = 0.5 * Measurement.ConvertUnit(speedInMicros * duration, Measurement.Unit.METER, MeasurementUnit);

                    Sampler?.ProvideSample(this, Distance);

                    message.AddValue(PARAM_DISTANCE, Distance);
                    message.AddValue(PARAM_UNITS, MeasurementUnit);
                }

            }
        } //end HandleMessage
    } //end class
}
