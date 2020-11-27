using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Utilities;

namespace Chetch.Arduino.Devices.RangeFinders
{
    abstract public class RangeFinderBase : ArduinoDevice
    {
        public int MinDistance { get; set; } = 0;
        public int MaxDistance { get; set; } = -1;
        public int Offset { get; set; } = 0;
        public int Range { get { return MaxDistance > 0 ? MaxDistance - MinDistance : -1; } }

        private double _distance;
        public double Distance
        {
            get { return _distance; }
            set
            {
                _distance = SanitiseDistance(value);
            }
        }
        public double AverageDistance
        {
            get
            {
                return Sampler == null ? 0 : Sampler.GetAverage(this);
            }
        }

        public double Percentage
        {
            get { return 100 * System.Math.Min(100, System.Math.Max(0, ((Distance - MinDistance) / Range))); }
        }

        public double AveragePercentage
        {
            get { return 100 * System.Math.Min(100, System.Math.Max(0, ((AverageDistance - MinDistance) / Range))); }
        }

        //constructor
        public RangeFinderBase(String id, String name) : base(id, name)
        {
            Category = DeviceCategory.RANGE_FINDER;

            TryAddCommand("read-distance", ArduinoCommand.CommandType.READ);
        }

        public override void RequestSample(Chetch.Utilities.Sampler sampler)
        {
            base.RequestSample(sampler);

            ExecuteCommand("read-distance");
        }

        protected double SanitiseDistance(double d)
        {
            double distance = d + Offset;
            if (distance < MinDistance) return MinDistance;
            if (MaxDistance > 0 && distance > MaxDistance) return MaxDistance;
            return distance;
        }
    }
}
