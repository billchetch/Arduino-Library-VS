﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Solid.Arduino.Firmata;

namespace Chetch.Arduino.Devices.Infrared
{
    public abstract class IRTransmitter : IRDevice
    {
        public const int BOARD_SPECIFIED = -1;

        private bool _enabled = false;
        private int _enablePin; //HIGH output means the transmitter is disabled (as there is no voltage across it)
        private int _transmitPin;

        //if last command is same as current command and time diff (millis) between last command and current command
        //is less than RepeatInterval then use _repeatCommand if it exists.
        private ArduinoCommand _repeatCommand = null;
        public int RepeatInterval { get; set; } = 200;
        public bool UseRepeatCommand = true;
        
        public IRTransmitter(String id, String name, int enablePin, int transmitPin, IRDB db = null) : base(id, name, db)
        {
            Category = DeviceCategory.IR_TRANSMITTER;

            _enablePin = enablePin;
            _transmitPin = transmitPin;
            ConfigurePin(_enablePin, PinMode.DigitalOutput);
            if (transmitPin != BOARD_SPECIFIED)
            {
                ConfigurePin(_transmitPin, PinMode.PwmOutput);
            }
        }

        public IRTransmitter(int enablePin, int transmitPin, IRDB db = null) : this("irt" + enablePin, "IRT", enablePin, transmitPin, db) { }

        public override void ReadDevice()
        {
            base.ReadDevice();
            if(DB != null)
            {
                ClearCommands();
                AddCommands(DB.GetCommands(Name));

                _repeatCommand = GetCommand(REPEAT_COMMAND);
            }
        }

        public void Disable()
        {
            Mgr.SetDigitalPin(_enablePin, true);
            _enabled = false;
        }

        public void Enable()
        {
            Mgr.SetDigitalPin(_enablePin, false);
            _enabled = true;
        }

        override public void ExecuteCommand(String commandAlias, List<Object> extraArgs = null)
        {
            if(commandAlias.Length == 2 && uint.TryParse(commandAlias, out _))
            {
                //split a 2 digit number in to it's components ... this is so we can have commands like 62
                //without needing to build a new command from '6' and from '2'
                int d1 = (int)char.GetNumericValue(commandAlias[0]);
                int d2 = (int)char.GetNumericValue(commandAlias[1]);

                base.ExecuteCommand(d1.ToString(), extraArgs);
                System.Threading.Thread.Sleep(RepeatInterval * 2);
                base.ExecuteCommand(d2.ToString(), extraArgs);
            } else
            {
                base.ExecuteCommand(commandAlias, extraArgs);
            }
        }

        override protected void ExecuteCommand(ArduinoCommand command, List<Object> extraArgs = null, bool deep = false)
        {
            if(!_enabled){
                List<ArduinoDevice> devices = Mgr.GetDevicesByPin(_transmitPin);
                foreach (var device in devices)
                {
                    if (device is IRTransmitter && device != this)
                    {
                        ((IRTransmitter)device).Disable();
                    }
                }

                Enable();
            }

            base.ExecuteCommand(command, extraArgs, deep);
        }

        override protected void SendCommand(ArduinoCommand command, List<Object> extraArgs = null)
        {
            if(command.Type == ArduinoCommand.CommandType.SEND && Protocol != IRProtocol.UNKNOWN && command.Arguments.Count == 3)
            {
                command.Arguments[2] = (int)Protocol;
            }

            var timeDiff = (DateTime.Now.Ticks - LastCommandSentOn) / TimeSpan.TicksPerMillisecond;
            if (UseRepeatCommand && _repeatCommand != null && LastCommandSent != null && LastCommandSent.Equals(command) && timeDiff < RepeatInterval)
            {
                base.SendCommand(_repeatCommand, extraArgs);
                LastCommandSent = command;
            }
            else
            {
                base.SendCommand(command, extraArgs);
            }
        }

        public override void HandleMessage(ADMMessage message)
        {
            //check if the transmit pin is viable
            if(_transmitPin == BOARD_SPECIFIED && message.HasValue("TP"))
            {
                int tp = message.GetInt("TP");
                if (!Mgr.IsPinCapable(tp, PinMode.PwmOutput))
                {
                    throw new Exception(String.Format("Device {0} is using pin {1} which is not capable for PWM output", ID, tp));
                }

                var devs = Mgr.GetDevicesByPin(tp);
                if (devs != null)
                {
                    foreach (var dev in devs)
                    {
                        if (dev != this && !dev.IsPinCompatible(tp, PinMode.PwmOutput))
                        {
                            throw new Exception(String.Format("Device {0} is using pin {1} which is not compatible with device {2} usage of this pin", ID, tp, dev.ID));
                        }
                    }
                }
                _transmitPin = tp;
                ConfigurePin(tp, PinMode.PwmOutput);
            }


            base.HandleMessage(message);
        }
    }
}
