using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Infrared
{
    public class IRDB : ArduinoCommandsDB
    {

        public IRDB()
        {
            //empty constructor for tempplate methods
        }


        override public void Initialize()
        {
            String fields = "dc.*, command_alias";
            String from = "ir_device_commands dc INNER JOIN ir_devices d ON dc.device_id=d.id INNER JOIN ir_commands c ON dc.command_id=c.id";
            String filter = "device_name='{0}'";
            String sort = "dc.id";
            this.AddSelectStatement("ir_device_commands", fields, from, filter, sort);

            base.Initialize();
        }

        override protected List<Dictionary<String, Object>> SelectCommands(String deviceName)
        {
            return Select("ir_device_commands", "command, command_alias, bits, protocol, repeat_count", deviceName);
        }

        protected override ArduinoCommand CreateCommand(string deviceName, Dictionary<string, object> row)
        {
            var command = new ArduinoCommand((String)row["command_alias"], (uint)row["repeat_count"]);
            command.AddArgument(Convert.ToUInt64((String)row["command"], 16)); //assumes commands are given in hex format
            command.AddArgument(Convert.ToUInt16(row["bits"]));
            return command;
        }
    }
}
