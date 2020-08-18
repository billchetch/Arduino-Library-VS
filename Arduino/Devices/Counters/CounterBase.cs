using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Chetch.Utilities;

namespace Chetch.Arduino.Devices
{
    abstract public class CounterBase : SwitchSensor
    {
        public long Count { get; internal set; } = 0;
        private long _prevCount = 0;
        public Boolean BroadcastStateChange { get; set; } = false;

        public CounterBase(int pin, int noiseThreshold, String id, String name) : base(pin, noiseThreshold, id, name)
        {
            Category = DeviceCategory.COUNTER;
        }

        protected override void OnStateChange(bool newState)
        {
            if (newState)
            {
                Count++;
            }

            //Parent method just broadcasts state change... not necessary for a counter
            if (BroadcastStateChange)
            {
                base.OnStateChange(newState);
            }
        }


        public override void RequestSample(Sampler sampler)
        {
            base.RequestSample(sampler);

            sampler.ProvideSample(this, Count - _prevCount);
            _prevCount = Count;
        }
    }
}
