using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Application;
using Chetch.Utilities;
using Solid.Arduino.Firmata;

namespace Chetch.Arduino.Devices
{
    public class SwitchSensor : ArduinoDevice
    {
        private int _sensorPin;
        private bool _rawState = false; //the last reported pin value
        public bool State { get; internal set; } = false; //verified state
        private bool _latestState = false;
        private int _noiseThreshold; //time delay in mills for acceptable state change
        private Object _stateLock = new Object();

        public bool IsOn { get { return State; } }
        public bool IsOff { get { return !State;  } }

        public DateTime LastOn { get; set; }
        public DateTime LastOff { get; set; }

        public Messaging.MessageType BroadcastStateChangeAs { get; set; } = Messaging.MessageType.DATA; //message type to broadcast

        public SwitchSensor(int pin, int noiseThreshold, String id, String name) : base(id, name)
        {
            _sensorPin = pin;
            _noiseThreshold = noiseThreshold;
            ConfigurePin(_sensorPin, PinMode.DigitalInput, State); //, initialState ? 1 : 0);
        }

        public SwitchSensor(int pin, int noiseThreshold = 0) : this(pin, noiseThreshold, "switch" + pin, "SWSensor"){ }

        protected override void OnConnect(ADMMessage message)
        {
            base.OnConnect(message);

            RequestState();
        }

        public void RequestState()
        {
            //this will trigger firmata to return the digital state of the port
            Mgr.SetDigitalReportMode(Mgr.GetPortForPin(_sensorPin), true);
        }

        public override void HandleDigitalPinStateChange(int pinNumber, bool newState)
        {
            if (pinNumber != _sensorPin) throw new Exception(String.Format("State changed on pin {0} but sensor is attached to pin {1}", pinNumber, _sensorPin));
            
            //we keep a record of the raw data so if we re-enable then we can re-create a state change event
            _rawState = newState;
            if (!Enabled) return;
            
            if (newState != _latestState)
            {
                lock (_stateLock)
                {
                    _latestState = newState;
                }
                if (_noiseThreshold > 0)
                {
                    ThreadExecutionManager.Execute<bool>(ID, VerifyStateChange, _latestState);
                } else
                {
                    OnStateChange(_latestState);
                }
            }
        }

        private void VerifyStateChange(bool state2verify)
        {
            System.Threading.Thread.Sleep(_noiseThreshold);
            if(state2verify == _latestState && State != _latestState)
            {
                //So we have waited some time and the state that triggered the veification is the same as
                //the latest pin state.  Furthermore this new state is different from the previous verified state
                //so we trigger on state change event
                lock (_stateLock)
                {
                    State = _latestState;
                }
                OnStateChange(_latestState);
            }

            //if the laatest pin state has changed since verification process began then try again
            if (state2verify != _latestState)
            {
                VerifyStateChange(_latestState);       
            }
        }

        virtual protected void OnStateChange(bool newState)
        {
            if (newState)
            {
                LastOn = DateTime.Now;
            } else
            {
                LastOff = DateTime.Now;
            }

            ADMMessage message  = new ADMMessage();
            message.Type = BroadcastStateChangeAs;
            message.AddValue("State", newState);
            Broadcast(message);
        }

        public override void RequestSample(Sampler sampler)
        {
            base.RequestSample(sampler);
            RequestState();
        }

        public void Enable(bool enable = true)
        {
            if (enable == Enabled) return; //to avoid triggring stuff twice

            base.Enable(enable);
            if (!Enabled)
            {
                State = false;
                _latestState = false;
            } else if (_rawState)
            { 
                HandleDigitalPinStateChange(_sensorPin, _rawState);
            }
        }
    }
}
