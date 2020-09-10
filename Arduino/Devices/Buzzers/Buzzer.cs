using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Devices.Buzzers
{
    public class Buzzer : Switch
    {
        private System.Timers.Timer _silenceTimer;
        private bool _silenced = false;
        public bool IsSilenced { get { return _silenced; } }

        public Buzzer(int pin, String id, String name) : base(pin, id, name)
        {
            _silenceTimer = new System.Timers.Timer();
            _silenceTimer.Elapsed += new System.Timers.ElapsedEventHandler(HandleSilenceTimer);

            TryAddCommand("silence");
        }

        public Buzzer(int pin) : this(pin, "buzzer" + pin, "BUZZER") { }

        public override void On()
        {
            if (_silenced)
            {
                //if silenced then set the state but don't set the pin
                State = true;
            }
            else
            {
                base.On();
            }
        }

        public void Silence(int duration)
        {
            if (duration <= 0) throw new ArgumentException("Duration must be a positive number");
            if (_silenced) return;

            _silenceTimer.Interval = duration;
            _silenced = true;
            //so turn off the pin that controls the buzzer but leave the 'Switch State' the same as that
            //can be set by outside influcence
            SetPin(false);
            _silenceTimer.Start();
        }

        public void Unsilence()
        {
            _silenceTimer.Stop();
            _silenced = false;
            SetPin(State);
        }

        private void HandleSilenceTimer(Object sender, System.Timers.ElapsedEventArgs ea)
        {
            //turn off timer and set buzzer pin to whatever the buzzer state really is 
            Unsilence();
            ADMMessage message = new ADMMessage();
            message.Type = Messaging.MessageType.NOTIFICATION;
            message.Value = "Silenced timer ended";
            Broadcast(message);
        }

        protected override void ExecuteCommand(ArduinoCommand command, ExecutionArguments xargs)
        {
            switch(command.CommandAlias.ToLower()){
                case "silence":
                    int duration = xargs.GetInt(0, 10);
                    Silence(duration);
                    break;

                case "unsilence":
                    Unsilence();
                    break;

                default:
                    base.ExecuteCommand(command, xargs);
                    break;
            }
        }
    }
}
