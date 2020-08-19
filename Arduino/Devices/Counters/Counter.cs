using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Devices.Counters
{
    public class Counter : CounterBase
    {

        public double AverageCount
        {
            get { return SampledAverage; }
        }

        public Counter(int pin, int noiseThreshold, String id, String name) : base(pin, noiseThreshold, id, name){}

        public Counter(int pin, int noiseThreshold = 0) : this(pin, noiseThreshold, "ctr" + pin, "Counter"){ }
    }
}
