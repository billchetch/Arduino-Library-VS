using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Application;
using Solid.Arduino.Firmata;

namespace Chetch.Arduino.Devices
{
    public class SwitchSensor : ArduinoDevice
    {
        private int _sensorPin;
        public bool State { get; internal set; } = false; //verified state
        private bool _latestState = false;
        private int _noiseThreshold; //time delay in mills for acceptable state change
        private Object _stateLock = new Object();
        
        public SwitchSensor(int pin, int noiseThreshold, String id, String name) : base(id, name)
        {
            _sensorPin = pin;
            _noiseThreshold = noiseThreshold;
            ConfigurePin(_sensorPin, Solid.Arduino.Firmata.PinMode.DigitalInput); //, initialState ? 1 : 0);
        }

        public SwitchSensor(int pin, int noiseThreshold = 0) : this(pin, noiseThreshold, "switch" + pin, "SwitchSensor")
        {
            
        }

        protected override void OnConnect(ADMMessage message)
        {
            base.OnConnect(message);

            Mgr.SetDigitalReportMode(Mgr.GetPortForPin(_sensorPin), true);
        }

        public override void HandleDigitalPinStateChange(int pinNumber, bool newState)
        {
            if (pinNumber != _sensorPin) throw new Exception(String.Format("State changed on pin {0} but sensor is attached to pin {1}", pinNumber, _sensorPin));
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
            ADMMessage message  = new ADMMessage();
            message.Type = Messaging.MessageType.DATA;
            message.AddValue("State", newState);
            Broadcast(message);
        }
    }
}
