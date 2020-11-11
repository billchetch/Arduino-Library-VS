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
        public double Calibration { get; set; } = 1;
        public double AverageRPM
        {
            get
            {
                double averageRPM = AverageRate * 60;
                return Calibration * averageRPM;
            }
        }

        public RPMCounter(int pin, String id, String name) : base(pin, id, name)
        {
            SampleInterval = 1000; //in ms
            SampleSize = 5;
            SamplingOptions = Sampler.SamplingOptions.MEAN_COUNT;

            CountMode = Mode.RATE;
        }

        public RPMCounter(int pin) : this(pin, "rpm" + pin, "RPM"){}
        
    }
}
