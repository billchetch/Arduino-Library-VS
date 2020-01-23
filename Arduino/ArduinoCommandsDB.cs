﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino
{
    abstract public class ArduinoCommandsDB : Database.DB
    {
        public ArduinoCommandsDB(String server, String db, String username, String password) : base(server, db, username, password)
        {
            
        }

        abstract protected List<Dictionary<String, Object>> SelectCommands(String deviceName);
        abstract protected ArduinoCommand CreateCommand(String deviceName, Dictionary<String, Object> row);

        public Dictionary<String, ArduinoCommand> GetCommands(String deviceName)
        {
            if (deviceName == null || deviceName.Length == 0 || deviceName == "") throw new Exception("Cannot get commands if no device name is given");
            Dictionary<String, ArduinoCommand> commands = new Dictionary<string, ArduinoCommand>();

            var rows = SelectCommands(deviceName);
            foreach (var row in rows)
            {
                var command = CreateCommand(deviceName, row);
                commands[command.CommandAlias] = command;
            }

            return commands;
        }
    }
}
