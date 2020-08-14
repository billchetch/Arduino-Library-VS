using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Devices.Counters
{
    public class Counter : CounterBase
    {

        public long CountPerSecond
        {
            get { return CountPerInterval; }
        }

        public double AveragePerSecond
        {
            get { return AveragePerInterval; }
        }

        public Counter(int pin, int noiseThreshold, String id, String name) : base(pin, noiseThreshold, id, name)
        {
            Interval = 1000;
        }

        public Counter(int pin, int noiseThreshold = 0) : this(pin, noiseThreshold, "counter" + pin, "Counter")
        {

        }
    }
}
