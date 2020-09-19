using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Utilities;

namespace Chetch.Arduino.Devices.Counters
{
    public class RPMCounter : Counter
    {
        public int SampleInterval { get; set; } = 1000; //in ms
        public int SampleSize { get; set; } = 5;

        public double AverageRPM
        {
            get
            {
                return AverageCount * 60000/SampleInterval;
            }
        }

        public RPMCounter(int pin, String id, String name) : base(pin, 0, id, name)
        {
            
        }

        public RPMCounter(int pin) : this(pin, "rpm" + pin, "RPM"){}

        public override void AddConfig(ADMMessage message)
        {
            base.AddConfig(message);
            if(SampleInterval <= 0 || SampleSize <= 0)
            {
                throw new ArgumentException("Incorrect sample argument values");
            }

            ConfigureSampler(SampleInterval, SampleSize);
        }
    }
}
