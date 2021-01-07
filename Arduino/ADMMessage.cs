using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Messaging;

namespace Chetch.Arduino
{
    public class ADMMessage : Message
    {
        public class MessageTags
        {
            const long TTL = 5 * 1000; //how long in millis a Tag can last for 
            private long[] _usedTags = new long[255];

            public void Release(byte tag)
            {
                if (tag == 0) return;
                _usedTags[tag] = 0;
            }

            public byte CreateTag()
            {
                //start from 1 as we reserve 0 to mean a non-assigned tag
                long nowInMillis = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                for (byte i = 1; i < _usedTags.Length; i++)
                {
                    if (_usedTags[i] == 0 || nowInMillis - _usedTags[i] > TTL)
                    {
                        _usedTags[i] = nowInMillis;
                        return i;
                    }
                }
                throw new Exception("Cannot create tag as all tags are being used");
            }

            public int Available
            {
                get
                {
                    int available = 0;
                    long nowInMillis = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                    for (byte i = 1; i < _usedTags.Length; i++)
                    {
                        if (_usedTags[i] == 0 || nowInMillis - _usedTags[i] > TTL)
                        {
                            available++;
                        }
                    }
                    return available;
                }
            }

            public void Reset()
            {
                for (byte i = 1; i < _usedTags.Length; i++)
                {
                    _usedTags[i] = 0;
                }
            }
        }

        public static byte CreateCommandID(byte ctype, byte idx)
        {
            byte cid = (byte)((int)ctype + (idx << 4));
            return cid;
        }

        public static bool IsCommandType(byte commandID, byte ctype)
        {
            return GetCommandType(commandID) == ctype;
        }

        public static byte GetCommandType(byte commandID)
        {
            return (byte)(commandID & 0xF);
        }

        public static byte GetCommandIndex(byte commandID)
        {
            return (byte)(commandID >> 4);
        }

        public byte Tag { get; set; } = 0; //can be used to track messages
        public byte TargetID { get; set; } = 0; //ID number on board to determine what is beig targeted
        public byte CommandID { get; set; } = 0; //Command ID on board ... basically to identify function e.g. Send or Delete ...
        public byte SenderID { get; set; } = 0; //
        public List<byte[]> Arguments { get; } = new List<byte[]>();
        public bool LittleEndian { get; set; } = true;
        public bool CanBroadcast { get; set; } = true;

        public ADMMessage(byte tag)
        {
            DefaultEncoding = MessageEncoding.BYTES_ARRAY;
            if (tag > 0) Tag = tag;
        }

        public ADMMessage() : this(0) { }

        public override void AddBytes(List<byte> bytes)
        {
            //Scheme here is 4 bytes as a 'header' to include: Type, Tag, TargetID, CommandID
            //Followed by a non-limited (although in reality it should be determined by the board)
            //number of arguments, where each argument is preceded by the number of bytes the argument needs
            //and then finally a checksum of all the bytes.  Then calculate a zerobyte and covert all the 0s to that
            //byte finally put the zerobyte at the beginning of the message

            //1. Add all the bytes
            base.AddBytes(bytes); //will add Type

            bytes.Add(Tag);
            bytes.Add(TargetID);
            bytes.Add(CommandID);
            bytes.Add(SenderID);

            foreach (var b in Arguments)
            {
                bytes.Add((byte)b.Length);
                bytes.AddRange(b);
            }

            //2. Calculate checkxum and add it to end.            
            byte checksum = Utilities.CheckSum.SimpleAddition(bytes.ToArray());
            bytes.Add(checksum);

            if (bytes.Count > 254) throw new Exception("Message cannot exceed 254 bytes");

            //3. Calculate a zerobyte
            byte zeroByte = (byte)1;
            while (zeroByte <= 255)
            {
                bool useable = true;
                foreach (var b in bytes)
                {
                    if (b == zeroByte)
                    {
                        useable = false;
                        break;
                    }
                }
                if (useable) break;
                zeroByte++;
            }

            //4. Use the zerobyte to convert all 0s to that byte then put the zerobyte at the beginning of the array
            if (zeroByte >= 1)
            {
                for (int i = 0; i < bytes.Count; i++)
                {
                    if (bytes[i] == 0) bytes[i] = zeroByte;
                }

            }
            else
            {
                throw new Exception("Unable to generate mask for byte array");
            }

            //5. Put the zerobyte at the beginning of the array
            bytes.Insert(0, zeroByte);
        }

        public void AddArgument(byte[] bytes)
        {
            Arguments.Add(bytes);
        }

        public void AddArgument(byte b)
        {
            AddArgument(new byte[] { b });
        }

        public void AddArgument(String s)
        {
            AddArgument(Chetch.Utilities.Convert.ToBytes(s));
        }

        public void AddArgument(int arg)
        {
            byte[] bytes = Chetch.Utilities.Convert.ToBytes((ValueType)arg, LittleEndian);
            AddArgument(bytes);
        }

        public byte[] Argument(int argIdx)
        {
            return Arguments[argIdx];
        }

        public byte ArgumentAsByte(int argIdx)
        {
            return Argument(argIdx)[0];
        }

        public bool ArgumentAsBool(int argIdx)
        {
            return ArgumentAsByte(argIdx) > 0;
        }

        public int ArgumentAsInt(int argIdx)
        {
            return Chetch.Utilities.Convert.ToInt(Argument(argIdx));
        }

        public long ArgumentAsLong(int argIdx)
        {
            return Chetch.Utilities.Convert.ToLong(Argument(argIdx));
        }

        public float ArgumentAsFloat(int argIdx)
        {
            return Chetch.Utilities.Convert.ToFloat(Argument(argIdx));
        }

        public String ArgumentAsString(int argIdx)
        {
            return Chetch.Utilities.Convert.ToString(Argument(argIdx));
        }

        public override void OnDeserialize(string s, MessageEncoding encoding)
        {
            base.OnDeserialize(s, encoding);

            switch (encoding)
            {
                case MessageEncoding.BYTES_ARRAY:
                    byte[] bytes;
                    try
                    {
                        bytes = Chetch.Utilities.Convert.ToBytes(s);
                        if (bytes.Length < 7) throw new Exception("ADMMessage::onDeserialize message  only has " + bytes.Length + " bytes ... must have 7 or more");
                        
                        //1. get zerobyte and checkbyte
                        byte zeroByte = bytes[0];
                        byte checkbyte = bytes[bytes.Length - 1];

                        //2. replace zerobyte with 0 and calculate checksum
                        byte checksum = 0;
                        for (int i = 1; i < bytes.Length - 1; i++)
                        {
                            if (bytes[i] == zeroByte) bytes[i] = 0;
                            checksum += bytes[i];
                        }
                        if (checkbyte == zeroByte) checkbyte = 0;

                        //3. Confirm checkbyte equals checksum
                        if (checksum != checkbyte)
                        {
                            throw new Exception(String.Format("ADMMessage::onDeserialize checksum of {0} does not match checkbyte of {1}", checksum, checkbyte));
                        }

                        //By here we know we have valid message data so add propoerties...
                        Type = (Chetch.Messaging.MessageType)bytes[1];
                        Tag = bytes[2];
                        TargetID = bytes[3];
                        CommandID = bytes[4];
                        SenderID = bytes[5];

                        //... and convert arguments
                        int argumentIndex = 6; // 1 more than Type, Tag, Target, Command, Sender because first byte is zero byte
                        while (argumentIndex < bytes.Length - 1) //1 less than all the bytes cos last byte is checksum
                        {
                            int length = bytes[argumentIndex];
                            byte[] arg = new byte[length];
                            for (int i = 0; i < length; i++)
                            {
                                arg[i] = bytes[argumentIndex + i + 1];
                            }
                            AddArgument(arg);
                            argumentIndex += length + 1;
                        }
                    }
                    catch (Exception e)
                    {
                        throw e;
                    }
                    break;
            } //end encoding switch
        }
    }
}
