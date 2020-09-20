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
                long durationTotal = Mgr.Sampler.GetDurationTotal(this);
                double averageRPM = Mgr.Sampler.GetSampleTotal(this) * 60000 / (double)durationTotal;
                return Calibration * averageRPM;
            }
        }

        public RPMCounter(int pin, String id, String name) : base(pin, id, name)
        {
            SampleInterval = 1000; //in ms
            SampleSize = 5;
            SamplingOptions = Sampler.SamplingOptions.MEAN_INTERVAL;
        }

        public RPMCounter(int pin) : this(pin, "rpm" + pin, "RPM"){}
        
    }
}
