using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Infrared
{
    public class IRDB : Database.DB
    {
        public IRDB(String server, String db, String username, String password) : base(server, db, username, password)
        {
            String fields = "dc.*, command";
            String from = "ir_device_commands dc INNER JOIN ir_devices d ON dc.device_id=d.id INNER JOIN ir_commands c ON dc.command_id=c.id";
            String filter = "device_name='{0}'";
            String sort = "dc.id";
            this.AddSelectStatement("ir_device_commands", fields, from, filter, sort);
        }

        public Dictionary<String, String> GetCommands(String deviceName)
        {
            if (deviceName == null || deviceName.Length == 0 || deviceName == "") throw new Exception("Cannot get commands if no device name is given");

            Dictionary<String, String> commands = new Dictionary<string, string>();
            List<Dictionary<String, Object>> rows = Select("ir_device_commands", "command, command_string, bits, protocol", deviceName);
            foreach (var row in rows)
            {
                String commandName = (String)row["command"];
                String command4arduino = (String)(row["command_string"] + " " + row["bits"] + " " + row["protocol"]);
                commands[commandName] = command4arduino;
            }

            return commands;
        }
    }
}
