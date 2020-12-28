using Chetch.Arduino.Devices.RangeFinders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.DeviceGroups
{
    public class FluidTanks : Chetch.Arduino.ArduinoDeviceGroup
    {
        public enum FluidLevel
        {
            EMPTY,
            VERY_LOW,
            LOW,
            OK,
            FULL
        }

        public class FluidTank : JSN_SR04T
        {
            public static FluidLevel GetFluidLevel(int percentFull)
            {
                if (percentFull <= PERCENTAGE_PRECISION)
                {
                    return FluidLevel.EMPTY;
                }
                else if (percentFull <= 2 * PERCENTAGE_PRECISION)
                {
                    return FluidLevel.VERY_LOW;
                }
                else if (percentFull <= 4 * PERCENTAGE_PRECISION)
                {
                    return FluidLevel.LOW;
                }
                else if (percentFull <= 100 - PERCENTAGE_PRECISION)
                {
                    return FluidLevel.OK;
                }
                else
                {
                    return FluidLevel.FULL;
                }
            }

            public int Capacity { get; set; } = 0; //Capacity in L

            public int PercentFull
            {
                get
                {
                    return 100 - ((int)Math.Round(AveragePercentage / (double)PERCENTAGE_PRECISION) * PERCENTAGE_PRECISION);
                }
            }

            public int Remaining
            {
                get
                {
                    return (int)(((double)PercentFull / 100.0) * Capacity);
                }
            }

            public FluidLevel Level
            {
                get
                {
                    return GetFluidLevel(PercentFull);
                }
            }

            public FluidTank(int transmitPin, int receivePin, String id) : base(transmitPin, receivePin, id) { }


        }

        public const int PERCENTAGE_PRECISION = 1;
        public const int DEFAULT_SAMPLE_INTERVAL = 10000;
        public const int DEFAULT_SAMPLE_SIZE = 12;

        public int SampleInterval { get; set; } = DEFAULT_SAMPLE_INTERVAL;
        public int SampleSize { get; set; } = DEFAULT_SAMPLE_SIZE;

        public List<FluidTank> Tanks { get; } = new List<FluidTank>();

        public int PercentFull
        {
            get
            {
                if (Tanks.Count == 0) return 0;
                double percentFull = 100.0 * ((double)Remaining / (double)Capacity);
                return ((int)Math.Round(percentFull / (double)PERCENTAGE_PRECISION) * PERCENTAGE_PRECISION);
            }
        }

        public int Remaining
        {
            get
            {
                int totalRemaining = 0;
                foreach (var wt in Tanks)
                {
                    totalRemaining += wt.Remaining;
                }
                return totalRemaining;
            }
        }

        public int Capacity
        {
            get
            {
                int totalCapacity = 0;
                foreach (var wt in Tanks)
                {
                    totalCapacity += wt.Capacity;
                }
                return totalCapacity;
            }
        }

        public FluidLevel Level { get; set; }

        public FluidTanks(String id) : base(id, null) { }

        public FluidTank AddTank(String id, int transmitPin, int receivePin, int capacity, int minDistance = JSN_SR04T.MIN_DISTANCE, int maxDistance = JSN_SR04T.MAX_DISTANCE)
        {
            FluidTank ft = new FluidTank(transmitPin, receivePin, id);
            ft.Capacity = capacity;
            ft.MinDistance = minDistance;
            ft.MaxDistance = maxDistance;
            ft.Offset = 3;

            ft.SampleInterval = SampleInterval;
            ft.SampleSize = SampleSize;

            Tanks.Add(ft);
            AddDevice(ft);

            return ft;
        }
    }
}
