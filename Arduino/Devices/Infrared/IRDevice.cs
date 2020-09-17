using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Arduino.Devices.Infrared
{
    public struct IRCode
    {
        public long Code;
        public int Protocol;
        public int Bits;

        public override string ToString()
        {
            return Code.ToString() + " (" + Code.ToString("X") + ") " + Protocol.ToString() + " " + Bits.ToString();
        }
    }

    public enum IRProtocol{
        UNKNOWN = -1, //we use this for sending raw command data, using the command as an index for raw data arrays defined arduino side
        UNUSED = 0,
        RC5,
        RC6,
        NEC,
        SONY,
        PANASONIC,
        JVC,
        SAMSUNG,
        WHYNTER,
        AIWA_RC_T501,
        LG,
        SANYO,
        MITSUBISHI,
        DISH,
        SHARP,
        DENON,
        PRONTO,
        LEGO_PF
    }

    public class IRDevice : ArduinoDevice
    {
        public const String REPEAT_COMMAND = "_REPEAT";

        protected IRDB DB { get; set;  }
        protected long DBID { get; set;  }  = 0;
        public bool IsInDB
        {
            get
            {
                return DBID > 0;
            }
        }

        private String _deviceName;
        virtual public String DeviceName {
            get
            {
                return _deviceName;
            }
            set
            {
                _deviceName = value;
                if (DB != null && _deviceName != null) ReadDevice();
            }
        }
        public String DeviceType { get; set; } = null;
        public String Manufacturer { get; set; } = null;
        public IRProtocol Protocol { get; set; } = IRProtocol.UNKNOWN; //TODO: set this to force sending/recording in a different protocol

        public IRDevice(String id, String name, IRDB db = null) : base(id, name)
        {
            DB = db;
        }
        
        virtual public void ReadDevice()
        {
            if (DB == null) throw new Exception("No database available");
            if (DeviceName == null) throw new Exception("No device name given");

            DBID = 0;
            Database.DBRow dev = DB.GetDevice(DeviceName);

            //TODO: make it so that you can choose to overwrite data or not
            if (dev != null)
            {
                DBID = dev.ID;
                DeviceType = (String)dev["device_type"];
                Manufacturer = (String)dev["manufacturer"];
            }
        }

        virtual public void WriteDevice()
        {
            if (DB == null) throw new Exception("No database supplied");

            if(DeviceType == null || DeviceType.Length == 0)
            {
                throw new Exception("Cannot write to DB as device does not have a type");
            }

            if(DBID > 0)
            {
                //DB.
            } else
            {
                DBID = DB.InsertDevice(Name, DeviceType, Manufacturer == null ? "Unknown" : Manufacturer);
            }
        }
    }
}
