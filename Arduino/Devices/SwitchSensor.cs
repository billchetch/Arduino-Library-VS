using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Application;

namespace Chetch.Arduino.Devices
{
    public class SwitchSensor : ArduinoDevice
    {
        private int _sensorPin;
        public bool State { get; internal set; } = false;
        private int _noiseThreshold; //time delay in mills for acceptable state change


        public SwitchSensor(int pin, int noiseThreshold, String id, String name) : base(id, name)
        {
            _sensorPin = pin;
            _noiseThreshold = noiseThreshold;
            ConfigurePin(_sensorPin, Solid.Arduino.Firmata.PinMode.DigitalInput);
        }

        public SwitchSensor(int pin, int noiseThreshold = 50) : this(pin, noiseThreshold, "switch" + pin, "SwitchSensor")
        {
            
        }

        public override void HandleDigitalPinStateChange(int pinNumber, bool newState)
        {
            if (pinNumber != _sensorPin) throw new Exception(String.Format("State changed on pin {0} but sensor is attached to pin {1}", pinNumber, _sensorPin));
            if (newState != State)
            {
                State = newState;
                ThreadExecutionManager.Execute<bool>(ID, VerifyStateChange, State);
            }
        }

        private void VerifyStateChange(bool state)
        {
            System.Threading.Thread.Sleep(_noiseThreshold);
            bool check1 = state == State;
            System.Threading.Thread.Sleep(1);
            bool check2 = state == State;
            if (check1 && check2) 
            {
                //means the state change that triggered this verification is still the state and so is therefore a legitimate state change
                OnStateChange(State);
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
