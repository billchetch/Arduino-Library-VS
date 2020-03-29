using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Infrared
{
    public class IRDB : ArduinoCommandsDB
    {
        public enum IREncoding
        {
            LONG = 1,
            HEX
        }

        public IREncoding Encoding { get; set; } = IREncoding.LONG;
        
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

            fields = "dev.*";
            from = "ir_devices dev";
            filter = null;
            sort = "device_name";
            this.AddSelectStatement("ir_devices", fields, from, filter, sort);

            this.AddInsertStatement("ir_devices", "device_name='{0}',device_type='{1}',manufacturer='{2}'");

            this.AddUpdateStatement("ir_devices", "device_name='{0}',device_type='{1}',manufacturer='{2}'", "id={3}");

            base.Initialize();
        }

        override protected List<Dictionary<String, Object>> SelectCommands(String deviceName)
        {
            return Select("ir_device_commands", "command, command_alias, bits, protocol, repeat_count", deviceName);
        }

        protected override ArduinoCommand CreateCommand(string deviceName, Dictionary<string, object> row)
        {
            var command = new ArduinoCommand((String)row["command_alias"], (uint)row["repeat_count"]);
            command.Type = ArduinoCommand.CommandType.SEND;
            switch (Encoding)
            {
                case IREncoding.HEX:
                    command.AddArgument(Convert.ToUInt64((String)row["command"], 16));
                    break;

                case IREncoding.LONG:
                    command.AddArgument(Convert.ToUInt64((String)row["command"]));
                    break;
            }
            command.AddArgument(Convert.ToUInt16(row["bits"]));
            command.AddArgument(Convert.ToUInt16(row["protocol"]));
            return command;
        }

        public List<Dictionary<String, Object>> GetDevices()
        {
            return Select("ir_devices", "id, device_name, device_type, manufacturer");
        }

        public Dictionary<String, Object> GetDevice(String deviceName)
        {
            var devs = GetDevices();
            foreach (var dev in devs)
            {
                if (deviceName.Equals((String)dev["device_name"], StringComparison.OrdinalIgnoreCase))
                {
                    return dev;
                }
            }
            return null;
        }

        public bool HasDevice(String deviceName)
        {
            return GetDevice(deviceName) != null;
        }

        public long InsertDevice(String deviceName, String deviceType, String manufacturer = "Unknown")
        {
            if (HasDevice(deviceName))
            {
                throw new Exception("Cannot add device " + deviceName + " as it already exists");
            }
            return Insert("ir_devices", deviceName, deviceType, manufacturer);
        }

        public void UpdateDevice(long id, String deviceName, String deviceType, String manufacturer = "Unknown")
        {
            Update("ir_devices", deviceName, deviceType, manufacturer, id.ToString());
        }


    }
}
