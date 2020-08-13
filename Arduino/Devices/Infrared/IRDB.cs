using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Database;

namespace Chetch.Arduino.Infrared
{
    public class IRDB : ArduinoCommandsDB
    {
        public enum IREncoding
        {
            LONG = 1,
            HEX
        }

        static public IRDB Create(System.Configuration.ApplicationSettingsBase settings, IREncoding encoding = IREncoding.HEX)
        {
            IRDB db = DB.Create<IRDB>(settings);
            db.Encoding = encoding;
            return db;
        }

        public IREncoding Encoding { get; set; } = IREncoding.HEX;
        
        public IRDB()
        {
            //empty constructor for tempplate methods
        }


        override public void Initialize()
        {
            //SELECTS
            // - Device commands: IR codes etc.
            String fields = "dc.*, command_alias";
            String from = "ir_device_commands dc INNER JOIN ir_devices d ON dc.device_id=d.id INNER JOIN ir_commands c ON dc.command_id=c.id";
            String filter = "device_name='{0}'";
            String sort = "command_alias";
            this.AddSelectStatement("ir_device_commands", fields, from, filter, sort, null);
            
            // - Devices
            fields = "dev.*";
            from = "ir_devices dev";
            filter = null;
            sort = "device_name";
            this.AddSelectStatement("ir_devices", fields, from, filter, sort, null);

            // - Command Aliases
            fields = "cmd.*";
            from = "ir_commands cmd";
            filter = null;
            sort = "command_alias";
            this.AddSelectStatement("ir_commands", fields, from, filter, sort, null);

            //INSERTS
            this.AddInsertStatement("ir_devices", "device_name='{0}',device_type='{1}',manufacturer='{2}'");
            this.AddInsertStatement("ir_commands", "command_alias='{0}'");
            this.AddInsertStatement("ir_device_commands", "device_id={0},command_id={1},command='{2}',protocol={3},bits={4}");

            //UPDATES
            this.AddUpdateStatement("ir_devices", "device_name='{0}',device_type='{1}',manufacturer='{2}'", "id={3}");
            this.AddUpdateStatement("ir_device_commands", "device_id={0},command_id={1},command='{2}',protocol={3},bits={4}", "id={5}");

            //Init base
            base.Initialize();
        }

        override public List<DBRow> SelectCommands(String deviceName)
        {
            return Select("ir_device_commands", "id, command, command_alias, bits, protocol", deviceName);
        }

        protected override ArduinoCommand CreateCommand(string deviceName, DBRow row)
        {
            var command = new ArduinoCommand((String)row["command_alias"]);
            command.Type = ArduinoCommand.CommandType.SEND;
            switch (Encoding)
            {
                case IREncoding.HEX:
                    command.AddArgument(Convert.ToInt64((String)row["command"], 16));
                    break;

                case IREncoding.LONG:
                    command.AddArgument(Convert.ToInt64((String)row["command"]));
                    break;
            }
            command.AddArgument(Convert.ToUInt16(row["bits"]));
            command.AddArgument(Convert.ToUInt16(row["protocol"]));
            return command;
        }

        public List<DBRow> SelectDevices()
        {
            return Select("ir_devices", "id, device_name, device_type, manufacturer");
        }

        public DBRow GetDevice(String deviceName)
        {
            var devs = SelectDevices();
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


        public List<DBRow> SelectCommandAliases()
        {
            return Select("ir_commands", "id,command_alias");
        }

        public long InsertCommandAlias(String alias)
        {
            return Insert("ir_commands", alias);
        }

        public long InsertCommand(long deviceId, long aliasId, long irCode, int protocol, int bits)
        {
            String code = "";
            switch (Encoding)
            {
                case IREncoding.HEX:
                    code = irCode.ToString("X");
                    break;
                case IREncoding.LONG:
                    code = irCode.ToString();
                    break;
            }
            return Insert("ir_device_commands", deviceId.ToString(), aliasId.ToString(), code, protocol.ToString(), bits.ToString());
        }

        public void UpdateCommand(long devCommandId, long deviceId, long aliasId, long irCode, int protocol, int bits)
        {
            String code = "";
            switch (Encoding)
            {
                case IREncoding.HEX:
                    code = irCode.ToString("X");
                    break;
                case IREncoding.LONG:
                    code = irCode.ToString();
                    break;
            }

            Update("ir_device_commands", deviceId.ToString(), aliasId.ToString(), code, protocol.ToString(), bits.ToString(), devCommandId.ToString());
        }
    }
}
