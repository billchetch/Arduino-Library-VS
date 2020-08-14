using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace Chetch.Arduino.Devices
{
    abstract public class CounterBase : SwitchSensor
    {
        public long Count { get; internal set; } = 0;
        private Timer _timer = null;
        protected int Interval { get; set; } = 1000; //default timer interval in ms
        private long _prevCount = 0;
        private long _intervalCount = 0;
        public long CountPerInterval { get; internal set; } = 0;
        public double AveragePerInterval{ get; internal set; } = 0;
        public Boolean BroadcastStateChange { get; set; } = false;

        public CounterBase(int pin, int noiseThreshold, String id, String name) : base(pin, noiseThreshold, id, name)
        {

        }

        public void Start()
        {
            if (_timer == null)
            {
                _timer = new System.Timers.Timer();
                _timer.Interval = Interval;
                _timer.Elapsed += new System.Timers.ElapsedEventHandler(this.OnTimer);
            }
            else
            {
                _timer.Stop();
            }
            
            Count = 0;
            _intervalCount = 0;
            _timer.Start();
        }

        public void Stop()
        {
            if (_timer != null)
            {
                _timer.Stop();
            }
        }

        
        protected virtual void OnTimer(Object sender, ElapsedEventArgs eventArgs)
        {
            long now = DateTime.Now.Ticks;

            CountPerInterval = Count - _prevCount;
            _prevCount = Count;

            _intervalCount++;
            AveragePerInterval = ((AveragePerInterval * (double)(_intervalCount - 1)) + (double)CountPerInterval) / (double)_intervalCount;
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

    }
}
