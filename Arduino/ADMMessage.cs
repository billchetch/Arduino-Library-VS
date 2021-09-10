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
            class MessageTag
            {
                byte Tag = 0;
                long Created = 0;
                int TTL = 5 * 1000;

                public MessageTag(byte tag, int ttl)
                {
                    if (tag == 0) throw new ArgumentException("Tag value cannot be 0");
                    Tag = tag;
                    TTL = ttl;
                    Created = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                }

                public int RemainingTTL => (int)(TTL - ((DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond) - Created));

                public bool IsAvailable => RemainingTTL < 0;

            }

            const int TTL = 5 * 1000; //how long in millis a Tag can last for 
            private Dictionary<byte, MessageTag> _usedTags = new Dictionary<byte, MessageTag>();
            private Dictionary<byte, List<byte>> _tagSets = new Dictionary<byte, List<byte>>();


            
            /// <summary>
            /// Releasing the tag means that the byte value can be used again for new tags.  It should be done at the end of a message loop.
            /// The return value is so that the onward message tag value can be set depending on whether the tag is part of a tagset or not
            /// </summary>
            /// <param name="tag"></param>
            /// <returns></returns>
            public byte Release(byte tag)
            {
                if (tag == 0) return 0;
                if (_usedTags.ContainsKey(tag))
                {
                    _usedTags.Remove(tag);
                }

                foreach(KeyValuePair<byte, List<byte>> tagSet in _tagSets)
                {
                    if (tagSet.Value.Contains(tag))
                    {
                        return tagSet.Key;
                    }
                }
                return tag;
            }

            public byte CreateTag(int ttl = TTL)
            {
                //start from 1 as we reserve 0 to mean a non-assigned tag
                for (byte i = 1; i <= 255; i++)
                {
                    if(IsAvailable(i))
                    {
                        _usedTags[i] = new MessageTag(i, ttl);
                        return i;
                    }
                }
                throw new Exception("Cannot create tag as all tags are being used");
            }

            public byte CreateTagSet(int ttl)
            {
                byte tag = CreateTag(ttl);
                _tagSets[tag] = new List<byte>();
                _tagSets[tag].Add(tag);

                return tag;
            }

            public byte CreateTagInSet(byte tagKey)
            {
                if (!_tagSets.ContainsKey(tagKey))
                {
                    throw new InvalidOperationException(String.Format("Cannot create tag in set {0} as the set does not exists", tagKey));
                }


                MessageTag mt = _usedTags[tagKey];
                if (mt.IsAvailable)
                {
                    throw new InvalidOperationException(String.Format("Cannot create tag in set {0} as the set has already expired", tagKey));
                }

                //if the set is empty use the tagKey as the tag otherwise create a new tag
                byte tag = _tagSets[tagKey].Count == 0 ? tagKey : CreateTag(Math.Max(1000, mt.RemainingTTL));
                if (_tagSets[tagKey].Contains(tag))
                {
                    throw new Exception(String.Format("Tag set {0} already contains tag {1}", tagKey, tag));
                }
                _tagSets[tagKey].Add(tag);
                
                return tag;
            }



            public bool IsAvailable(byte tag)
            {
                return !_usedTags.ContainsKey(tag) || _usedTags[tag].IsAvailable;
            }

            public int Used
            {
                get
                {
                    int used = 0;
                    foreach (var mt in _usedTags.Values)
                    {
                        if (!mt.IsAvailable)
                        {
                            used++;
                        }
                    }

                    return used;
                }
            }

            public int Available
            {
                get
                {
                    return 255 - Used;
                }
            }

            public void Reset()
            {
                _usedTags.Clear();
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
            //1. Add all the bytes
            base.AddBytes(bytes); //will add Type

            //2. Add member vars
            bytes.Add(Tag);
            bytes.Add(TargetID);
            //bytes.Add(CommandID);
            bytes.Add(SenderID);

            //3. add arguments (length of argument followed by argment bytes)
            foreach (var b in Arguments)
            {
                bytes.Add((byte)b.Length);
                bytes.AddRange(b);
            }
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
            byte[] bytes = Chetch.Utilities.Convert.ToBytes((ValueType)arg, LittleEndian, -1);
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
                        if (bytes.Length < 4) throw new Exception("ADMMessage::onDeserialize message  only has " + bytes.Length + " bytes ... must have 4 or more");
                        
                        //By here we know we have valid message data so add propoerties...
                        Type = (Chetch.Messaging.MessageType)bytes[0];
                        Tag = bytes[1];
                        TargetID = bytes[2];
                        //CommandID = bytes[3];
                        SenderID = bytes[3];

                        //... and convert arguments
                        int argumentIndex = 4;
                        while (argumentIndex < bytes.Length)
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
