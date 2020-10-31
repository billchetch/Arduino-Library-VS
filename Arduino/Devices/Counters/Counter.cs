﻿using System;
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
        public const String COMMAND_READ_COUNT = "read-count";
        public const String PARAM_COUNT = "CT"; //The Count parameter name used by the board device
        public const String PARAM_INTERVAL = "IV"; //The Interval paramater name used by th eboard device
            
        public long Count { get; internal set; } = 0;
        public long Interval { get; internal set; } = 0;
        
        private int _counterPin;
        private int _countState = 0;
        
        public double AverageCount
        {
            get
            {
                return Mgr.Sampler.GetAverage(this);
            }
        }

        public Counter(int pin, String id, String name) : base(id, name)
        {
            _counterPin = pin;

            ConfigurePin(_counterPin, PinMode.DigitalInput, _countState > 0 ? 0 : 1); //, initialState ? 1 : 0);

            Category = DeviceCategory.COUNTER;
            
            TryAddCommand(COMMAND_READ_COUNT, ArduinoCommand.CommandType.READ, true);
        }

        public Counter(int pin) : this(pin, "ctr" + pin, "Counter") { }

        public override void RequestSample(Sampler sampler)
        {
            base.RequestSample(sampler);

            ExecuteCommand(COMMAND_READ_COUNT);
        }

        public override void AddConfig(ADMMessage message)
        {
            base.AddConfig(message);

            message.AddArgument(_counterPin);
            message.AddArgument(_countState);
        }

        public override void HandleMessage(ADMMessage message)
        {
            base.HandleMessage(message);

            if (message.Type == Chetch.Messaging.MessageType.COMMAND_RESPONSE && message.HasValue(PARAM_COUNT))
            {
                Count = message.GetLong(PARAM_COUNT);
                Interval = message.GetLong(PARAM_INTERVAL);
                Mgr.Sampler.ProvideSample(this, (double)Count, Interval);
            }
        }
    } //end class
}
