using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Database;

namespace Chetch.Arduino
{
    abstract public class ArduinoCommandsDB : DB
    {
        public ArduinoCommandsDB()
        {
            //empty constructor for template factory method
        }

        abstract public List<DBRow> SelectCommands(String deviceName);
        virtual public DBRow SelectCommand(String deviceName, String commandAlias, String commandAliasField = "command_alias")
        {
            var commands = SelectCommands(deviceName);
            foreach(var row in commands)
            {
                if (commandAlias.Equals((String)row[commandAliasField], StringComparison.OrdinalIgnoreCase))
                {
                    return row;
                }
            }
            return null;
        }

        abstract protected ArduinoCommand CreateCommand(String deviceName, DBRow row);

        virtual public List<ArduinoCommand> GetCommands(String deviceName)
        {
            if (deviceName == null || deviceName.Length == 0 || deviceName == "") throw new Exception("Cannot get commands if no device name is given");
            List<ArduinoCommand> commands = new List<ArduinoCommand>();

            var rows = SelectCommands(deviceName);
            foreach (var row in rows)
            {
                var command = CreateCommand(deviceName, row);
                commands.Add(command);
            }

            return commands;
        }

        virtual public ArduinoCommand GetCommand(String deviceName, String commandAlias, String commandAliasField = "command_alias")
        {
            var row = SelectCommand(deviceName, commandAlias, commandAliasField);
            return row == null ? null : CreateCommand(deviceName, row);
        }
    }
}
