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
                return Calibration * AverageCount * 60000/SampleInterval;
            }
        }

        public RPMCounter(int pin, String id, String name) : base(pin, 0, id, name)
        {
            SampleInterval = 1000; //in ms
            SampleSize = 5;
        }

        public RPMCounter(int pin) : this(pin, "rpm" + pin, "RPM"){}
        
    }
}
