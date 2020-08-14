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
        protected int SampleSize { get; set; } = 60; //how many intervals to collect for a sample
        private long _prevCount = 0;
        private List<long> _counts = new List<long>();
        public long CountPerInterval { get; internal set; } = 0;
        public Boolean BroadcastStateChange { get; set; } = false;

        public long CountsPerSample
        {
            get
            {
                if (_counts.Count == 0)
                {
                    return 0;
                }
                else
                {
                    long n = _counts[_counts.Count - 1] - _counts[0];
                    return _counts.Count >= SampleSize ? n : (long)((double)(_counts.Count / SampleSize)*(double)n);
                }
            }
        }

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
            _counts.Clear();
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

            if(_counts.Count >= SampleSize)
            {
                _counts.RemoveAt(0);
            }
            _counts.Add(Count);
            CountPerInterval = Count - _prevCount;
            _prevCount = Count;
        }

        protected override void OnStateChange(bool newState)
        {
            Count++;

            //Parent method just broadcasts state change... not necessary for a counter
            if (BroadcastStateChange)
            {
                base.OnStateChange(newState);
            }
        }

    }
}
