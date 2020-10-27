using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Utilities;
using Solid.Arduino;

namespace Chetch.Arduino
{
    public class SharedSerialConnection : ISerialConnection
    {
        private const int ID_BYTE = 0xA0;

        private static Dictionary<String, List<SharedSerialConnection>> _connections = new Dictionary<String, List<SharedSerialConnection>>();
        private static Dictionary<String, SerialPorts.SerialPort> _serialPorts = new Dictionary<String, SerialPorts.SerialPort>();
        private static SharedSerialConnection _activeConnection;

        static private void OnDataReceived(object sender, SerialDataReceivedEventArgs eargs)
        {
            //read first byte then find the channel for the port
            if (sender is SerialPorts.SerialPort)
            {
                SerialPorts.SerialPort sp = (SerialPorts.SerialPort)sender;
                String port = sp.PortName;
                if (_connections.ContainsKey(port))
                {
                    int b = sp.PeekByte();
                    if ((b & 0xF0) == ID_BYTE) //determine 'type' of byte
                    {
                        int id = b & 0x0F; //get bottom 4 bits to specify ID
                        Console.WriteLine("Byte {0} provides connection ID {1}...", b.ToString("X"), id);
                        foreach (SharedSerialConnection scnn in _connections[port])
                        {
                            //scnn.HandleDataReceived(sender, eargs);
                            if (scnn.ID == id)
                            {
                                Console.WriteLine("Setting active connection to {0}", id);
                                _activeConnection = scnn;
                            }
                        }
                        sp.ReadByte(); //remove the ID byte fro the stream
                    }
                    _activeConnection?.HandleDataReceived(sender, eargs);
                }
            } //end check for Serial port
        }


        public int ID { get; internal set; } = 0;
        public byte IDCommand { get; internal set; } = 0;
        public int BaudRate { get => _serialPort.BaudRate; set => throw new Exception("SharedSerialConnection: cannot assign baud rate"); }
        public string PortName { get => _serialPort.PortName; set => throw new Exception("SharedSerialConnection: cannot assign portname"); }

        public bool IsOpen => _serialPort.IsOpen;

        public string NewLine { get => _serialPort.NewLine; set => _serialPort.NewLine = value; }

        public int BytesToRead { get => _serialPort.BytesToRead; }

        private SerialPorts.SerialPort _serialPort;

        public SharedSerialConnection(int id, String port, SerialBaudRate baudRate)
        {
            if (id <= 0 || id > 15) throw new Exception("ID must be between 1 and 15 inclusive");

            //check no other connections have this ID  on this port
            if (!_connections.ContainsKey(port)) _connections[port] = new List<SharedSerialConnection>();

            foreach (SharedSerialConnection ssc in _connections[port])
            {
                if (ssc.ID == id) throw new Exception(String.Format("There already exists a shared connection with ID {0} on port {1}", id, port));
            }

            if (!_serialPorts.ContainsKey(port))
            {
                SerialPorts.SerialPort sp = new SerialPorts.SerialPort(port, (int)baudRate);
                sp.DataReceived += OnDataReceived;
                _serialPorts[port] = sp;

            }

            ID = id;
            IDCommand = (byte)(ID_BYTE + ID);
            _serialPort = _serialPorts[port];

            _connections[port].Add(this);
        }

        public event SerialDataReceivedEventHandler DataReceived;

        public void HandleDataReceived(Object sender, SerialDataReceivedEventArgs eargs)
        {
            DataReceived?.Invoke(sender, eargs);
        }


        private byte[] createBufferWithID(byte[] buffer)
        {
            byte[] b2r = new byte[buffer.Length + 1];
            b2r[0] = IDCommand;
            for (int i = 0; i < buffer.Length; i++)
            {
                b2r[i + 1] = buffer[i];
            }
            return b2r;
        }

        public void Open()
        {
            _serialPort.Open();
        }

        public void Close()
        {
            _serialPort.Close();
        }

        public int ReadByte()
        {
            return _serialPort.ReadByte();
        }

        public void Write(String text)
        {
            //_serialPort.Write(text);
            Console.WriteLine("oops... write string");
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (offset == 0)
            {
                byte[] bufferWithID = createBufferWithID(buffer);
                _serialPort.Write(bufferWithID, offset, count + 1); //What to do if offset is non-zero???
                Console.WriteLine("Write: {0}", BitConverter.ToString(bufferWithID));
            }
            else
            {
                throw new Exception("SharedSerialConnection::Write .. cannot have a non-zero offset");
            }
        }

        public void Write(char[] buffer, int offset, int count)
        {
            //sendID();
            //_serialPort.Write(buffer, offset, count);
            Console.WriteLine("oops... write char buffer");
        }

        public void WriteLine(string text)
        {
            //_serialPort.WriteLine(text);
            Console.WriteLine("oops... write line");
        }

        public void Dispose()
        {
            _serialPort.Dispose();
        }
    }
}
