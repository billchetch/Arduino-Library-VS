using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Infrared
{
    public class IRDB : ArduinoCommandsDB
    {
        public IRDB(String server, String db, String username, String password) : base(server, db, username, password)
        {
            String fields = "dc.*, command";
            String from = "ir_device_commands dc INNER JOIN ir_devices d ON dc.device_id=d.id INNER JOIN ir_commands c ON dc.command_id=c.id";
            String filter = "device_name='{0}'";
            String sort = "dc.id";
            this.AddSelectStatement("ir_device_commands", fields, from, filter, sort);
        }

        override protected List<Dictionary<String, Object>> SelectCommands(String deviceName)
        {
            return Select("ir_device_commands", "command, command_string, bits, protocol", deviceName);
        }

        protected override ArduinoCommand CreateCommand(string deviceName, Dictionary<string, object> row)
        {
            var command = new ArduinoCommand();

            command.CommandAlias = (String)row["command"];
            command.Command = (String)(row["command_string"] + " " + row["bits"] + " " + row["protocol"]);

            return command;
        }
    }
}
