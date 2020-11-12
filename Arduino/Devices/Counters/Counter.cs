using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Chetch.Utilities;
using Solid.Arduino.Firmata;

namespace Chetch.Arduino.Devices.Counters
{
    public class Counter : ArduinoDevice
    {
        public enum Mode
        {
            COUNT = 0,
            RATE = 1
        }

        public const String COMMAND_READ_COUNT = "read-count";
        public const String COMMAND_READ_RATE = "read-rate";
        public const String PARAM_COUNT = "Count"; //The Count parameter name used by the board device
        public const String PARAM_INTERVAL = "Interval"; //The Interval paramater name used by th eboard device
        public const String PARAM_RATE = "Rate";
            
        public long Count { get; internal set; } = 0;
        public long Interval { get; internal set; } = 0;
        public float Rate { get; internal set; } = 0;
        public int RateInterval { get; set; } = 1000; //ms to count over (max val for int 2 bytes)

        private int _counterPin;
        private int _countState = 0;
        
        public double AverageCount
        {
            get
            {
                return Mgr.Sampler.GetAverage(this);
            }
        }

        public double AverageRate
        {
            get
            {
                return Mgr.Sampler.GetAverage(this);
            }
        }

        public Mode CountMode { get; set; } = Mode.COUNT;

        public Counter(int pin, String id, String name) : base(id, name)
        {
            _counterPin = pin;

            ConfigurePin(_counterPin, PinMode.DigitalInput, _countState > 0 ? 0 : 1); //, initialState ? 1 : 0);

            Category = DeviceCategory.COUNTER;
            
            //Important! these must be added in order of the Mode enum
            TryAddCommand(COMMAND_READ_COUNT, ArduinoCommand.CommandType.READ, true);
            TryAddCommand(COMMAND_READ_RATE, ArduinoCommand.CommandType.READ, true);
        }

        public Counter(int pin) : this(pin, "ctr" + pin, "Counter") { }

        public override void RequestSample(Sampler sampler)
        {
            base.RequestSample(sampler);

            switch (CountMode)
            {
                case Mode.COUNT:
                    ExecuteCommand(COMMAND_READ_COUNT);
                    break;
                case Mode.RATE:
                    ExecuteCommand(COMMAND_READ_RATE);
                    break;
            }
            
        }

        public override void AddConfig(ADMMessage message)
        {
            base.AddConfig(message);

            message.AddArgument(_counterPin);
            message.AddArgument(_countState);
            message.AddArgument(RateInterval);
        }

        public override void HandleMessage(ADMMessage message)
        {
            base.HandleMessage(message);

            if (ADMMessage.IsCommandType(message.CommandID, (byte)ArduinoCommand.CommandType.READ))
            {
                Mode mode = (Mode)ADMMessage.GetCommandIndex(message.CommandID);
                switch (mode)
                {
                    case Mode.COUNT:
                        Count = message.ArgumentAsLong(0);
                        Interval = message.ArgumentAsLong(1);
                        Mgr.Sampler.ProvideSample(this, (double)Count, Interval);

                        message.AddValue(PARAM_COUNT, Count);
                        message.AddValue(PARAM_INTERVAL, Interval);
                        break;

                    case Mode.RATE:
                        Rate = message.ArgumentAsFloat(0);
                        Mgr.Sampler.ProvideSample(this, (double)Rate);

                        message.AddValue(PARAM_RATE, Rate);
                        break;
                }
                
            }
        }
    } //end class
}
