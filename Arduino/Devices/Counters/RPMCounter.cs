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

        public double AverageRPM
        {
            get
            {
                return AverageCount * 60;
            }
        }

        public RPMCounter(int pin, String id, String name) : base(pin, 0, id, name)
        {
            
        }

        public RPMCounter(int pin) : this(pin, "rpm" + pin, "RPM"){}

        public override void AddConfig(ADMMessage message)
        {
            base.AddConfig(message);

            ConfigureSampler(1000, 5);
        }
    }
}
