﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Infrared
{
    public class IRLGHomeTheater : IRTransmitter
    {
        const String NAME = "LG Home Theater";

        public IRLGHomeTheater(int enablePin, int transmitPin, IRDB db) : base(NAME, enablePin, transmitPin, db)
        {
            AddCommand("Unmute", new String[]{"Volume_up","Volume_down"});
            AddCommand("Mute", new String[] { "Unmute", "Mute/Unmute" });
        }
    }
}
