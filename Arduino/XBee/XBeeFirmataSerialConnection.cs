using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using XBeeLibrary.Core;
using XBeeLibrary.Core.Models;
using XBeeLibrary.Core.Utils;

namespace Chetch.Arduino.XBee
{
    public class XBeeFirmataSerialConnection : Solid.Arduino.ISerialConnection
    {
        static private Dictionary<String, List<XBeeFirmataSerialConnection>> _connections = new Dictionary<String, List<XBeeFirmataSerialConnection>>();

        public bool IsOpen { get; internal set; } = false;

        public int BaudRate { get => XBSerialConnection.BaudRate; set => XBSerialConnection.BaudRate = value; }
        public string PortName { get => XBSerialConnection.PortName; set => XBSerialConnection.PortName = value; }
        public string NewLine { get => XBSerialConnection.NewLine; set => XBSerialConnection.NewLine = value; }

        public Queue<int> DataBuffer { get; internal set; } = new Queue<int>();

        public int BytesToRead { get => DataBuffer.Count; }

        public XBeeSerialConnection XBSerialConnection { get; internal set; }

        public XBeeDevice XBCoordinator { get; internal set; }

        public String NodeID { get; internal set; }

        public RemoteXBeeDevice XBRemoteDevice { get; set; }

        private Object lockXBDevice = new object();

        public XBeeFirmataSerialConnection(String nodeID, String port, Solid.Arduino.SerialBaudRate baudRate)
        {
            if (String.IsNullOrEmpty(nodeID)) throw new Exception("Connection must specify a Node ID");

            NodeID = nodeID;

            //try get a connection from the pool
            if (_connections.ContainsKey(port) && _connections[port].Count > 0)
            {
                //do a quick check to see if the nodeID is being used
                foreach (var cnn in _connections[port])
                {
                    if (cnn.NodeID.Equals(nodeID)) throw new Exception(String.Format("Node ID {0} is already taken for connections on port {1}", nodeID, port));
                }

                //reuse the connection and coordinator
                XBSerialConnection = _connections[port][0].XBSerialConnection;
                XBCoordinator = _connections[port][0].XBCoordinator;
            }
            else
            {
                //nothing on this port yet ... so we create a new serial coneection (for the port)
                //and a new coordinator (to coordinate XBee network)
                XBSerialConnection = new XBeeSerialConnection(port, (int)baudRate);
                XBCoordinator = new ZigBeeDevice(XBSerialConnection); //TODO: parameterize this

                _connections[port] = new List<XBeeFirmataSerialConnection>();
            }

            _connections[port].Add(this);
        }

        public event SerialDataReceivedEventHandler DataReceived;

        public void AddDataReceived(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                DataBuffer.Enqueue((int)data[i]);
            }

            Console.WriteLine("<------- XBLocalDevice received data {0} bytes", data.Length);

            //if we need the serial event args then this can be retreived (with some modification) from the serial port in the XBeeSerialConnection instance
            DataReceived?.Invoke(this, null);
        }

        private void HandleXBeeDataReceived(Object sender, XBeeLibrary.Core.Events.DataReceivedEventArgs args)
        {
            XBeeMessage msg = args.DataReceived;
            RemoteXBeeDevice rxb = msg.Device;
            XBee64BitAddress xaddr = msg.Device.XBee64BitAddr;
            byte[] received = msg.Data;

            Console.WriteLine("<----------- Data Received {0} bytes from {1}", received.Length, rxb.NodeID);
            if (NodeID.Equals(rxb.NodeID))
            {
                AddDataReceived(received);
            }
            else
            {
                Console.WriteLine("WTF... received data from {0} but this is node {1}", rxb.NodeID, NodeID);
            }
        }

        public void Open()
        {
            if (!XBCoordinator.IsOpen)
            {
                lock (lockXBDevice)
                {
                    XBCoordinator.Open();
                    XBCoordinator.ReceiveTimeout = 10000; //default is 2000
                }
            }

            XBCoordinator.DataReceived += HandleXBeeDataReceived;
            XBRemoteDevice = XBCoordinator.GetNetwork().DiscoverDevice(NodeID);
            if (XBRemoteDevice == null)
            {
                throw new Exception(String.Format("Open: Cannot find XBee with NodeID {0}", NodeID));
            }

            IsOpen = true;
        }

        public void Close()
        {
            XBCoordinator.DataReceived -= HandleXBeeDataReceived;
            IsOpen = false;

            bool allClosed = true;
            foreach (var cnn in _connections[PortName])
            {
                if (cnn.IsOpen)
                {
                    allClosed = false;
                    break;
                }
            }

            if (allClosed)
            {
                XBCoordinator.Close();
                XBSerialConnection.Close();
            }
        }

        public int ReadByte()
        {
            if (DataBuffer.Count == 0)
            {
                return -1;
            }
            else
            {
                return DataBuffer.Dequeue();
            }
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
                Console.WriteLine("-------> XBLocalDevice sending data {0} bytes to {1}", buffer.Length, XBRemoteDevice.NodeID);
                XBCoordinator.SendDataAsync(XBRemoteDevice, buffer);

                Console.WriteLine("-------> XBLocalDevice data sent to {0}", XBRemoteDevice.NodeID);
                //XBLocalDevice.SendBroadcastData(buffer);
            }
            else
            {
                throw new Exception("XBeeFirmataSerialConnection::Write .. cannot have a non-zero offset");
            }
        }

        public void Write(char[] buffer, int offset, int count)
        {
            Console.WriteLine("oops... write char buffer");
            throw new NotImplementedException("XBeeFirmataSericalConnection::Write char[] ");
        }

        public void WriteLine(string text)
        {
            Console.WriteLine("oops... write line");
            throw new NotImplementedException("XBeeFirmataSericalConnection::Write string ");
        }

        public void Dispose()
        {
            XBSerialConnection.Dispose();
        }
    }
}
