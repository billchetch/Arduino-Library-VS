﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using XBeeLibrary.Core;
using XBeeLibrary.Core.Models;
using XBeeLibrary.Core.Utils;
using Chetch.Arduino.Exceptions;
using XBeeLibrary.Core.Packet;
using XBeeLibrary.Core.Packet.Common;
using System.Threading;

namespace Chetch.Arduino.XBee
{
    public class XBeeFirmataSerialConnection : Solid.Arduino.ISerialConnection
    {
        private const int DELAY_AFTER_CONNECT = 2000; //how many ms to wait after a connection ... there can be reconnect issues if attempting to reconnect too soon
        private const int COORDINATOR_LOCK_TIMEOUT = 2000; //how long we wait to obatin a lock on the coordinator (e.g. for writing data)
        
        static private Dictionary<String, List<XBeeFirmataSerialConnection>> _connections = new Dictionary<String, List<XBeeFirmataSerialConnection>>();
        
        public bool IsOpen { get; internal set; } = false;

        public int BaudRate { get => XBSerialConnection.BaudRate; set => XBSerialConnection.BaudRate = value; }
        public string PortName { get => XBSerialConnection.PortName; set => XBSerialConnection.PortName = value; }
        public string NewLine { get => XBSerialConnection.NewLine; set => XBSerialConnection.NewLine = value; }

        private int _bufferReadPosition = 0;
        private int _bufferWritePosition = 0;

        private const int BUFFER_SIZE = 1024 * 1;
        public byte[] DataBuffer { get; internal set; } = new byte[BUFFER_SIZE]; //

        public int BytesToRead
        {
            get
            {
                if (_bufferWritePosition >= _bufferReadPosition)
                {
                    return _bufferWritePosition - _bufferReadPosition;
                } else
                {
                    return BUFFER_SIZE - _bufferReadPosition + _bufferWritePosition;
                }
            }
        }

        public int MaxRetrySendAttempts { get; set; } = 1;

        public XBeeSerialConnection XBSerialConnection { get; internal set; }

        public event SerialDataReceivedEventHandler DataReceived;
        
        public XBeeDevice XBCoordinator { get; internal set; }
        public Object XBCoordinatorLock;

        public String NodeID { get; internal set; }
        
        public RemoteXBeeDevice XBRemoteDevice { get; set; }

        private Object bufferPositionLock = new Object();
        private Task _deliverDataTask = null;

        public XBeeFirmataSerialConnection(String nodeID, String port, Solid.Arduino.SerialBaudRate baudRate)
        {
            if (String.IsNullOrEmpty(nodeID)) throw new Exception("Connection must specify a Node ID");

            NodeID = nodeID;

            //try get a connection from the pool
            if (_connections.ContainsKey(port) && _connections[port].Count > 0)
            {
                //do a quick check to see if the nodeID is being used
                XBeeFirmataSerialConnection cnn2remove = null;
                foreach (var cnn in _connections[port])
                {
                    if (cnn.NodeID.Equals(nodeID))
                    {
                        //if the connection is open we barf
                        if (cnn.IsOpen)
                        {
                            throw new Exception(String.Format("Node ID {0} is already being used for connections on port {1}", nodeID, port));
                        }
                        else 
                        { 
                            //otherwise we take this newly created connection and remove the old one
                            cnn2remove = cnn;
                        }
                        break;
                    }
                }

                //reuse the connection and coordinator and lock
                XBSerialConnection = _connections[port][0].XBSerialConnection;
                XBCoordinator = _connections[port][0].XBCoordinator;
                XBCoordinatorLock = _connections[port][0].XBCoordinatorLock;

                if (cnn2remove != null) _connections[port].Remove(cnn2remove);
            }
            else
            {
                //nothing on this port yet ... so we create a new serial coneection (for the port)
                //and a new coordinator (to coordinate XBee network)
                XBSerialConnection = new XBeeSerialConnection(port, (int)baudRate);
                XBCoordinator = new ZigBeeDevice(XBSerialConnection); //TODO: parameterize this
                XBCoordinatorLock = new Object();

                _connections[port] = new List<XBeeFirmataSerialConnection>();
            }

            _connections[port].Add(this);
        }

        private void FlushBuffer(bool resetBytes2zero = false)
        {
            _bufferReadPosition = 0;
            _bufferWritePosition = 0;
        }

        public void AddDataReceived(byte[] data)
        {
            if (data.Length > DataBuffer.Length) throw new Exception("Too many bytes!");

            int idx = _bufferWritePosition;
            for (int i = 0; i < data.Length; i++)
            {
                DataBuffer[idx] = data[i];
                idx = (idx + 1) % BUFFER_SIZE;
            }
            lock (bufferPositionLock)
            {
                _bufferWritePosition = idx;
            }

            //byte checksum = Chetch.Utilities.CheckSum.SimpleAddition(data);
            //Console.WriteLine("XBee: {0} added {1} bytes to buffer, checksum = {2}, rp = {3}, wp = {4}, available = {5}", NodeID, data.Length, checksum, _bufferReadPosition, _bufferWritePosition, BytesToRead);
            //Console.WriteLine("{0} XBee: {1}: {2} bytes: {3}", System.Threading.Thread.CurrentThread.ManagedThreadId, NodeID,data.Length, HexUtils.ByteArrayToHexString(data));
        }

        private void DeliverData()
        {
            while (IsOpen)
            {
                if (DataReceived != null && BytesToRead > 0)
                {
                    DataReceived.Invoke(this, null);
                }
                System.Threading.Thread.Sleep(10);
            }
        }

        private void HandleXBeeDataReceived(Object sender, XBeeLibrary.Core.Events.DataReceivedEventArgs args)
        {
            XBeeMessage msg = args.DataReceived;
            RemoteXBeeDevice rxb = msg.Device;
            XBee64BitAddress xaddr = msg.Device.XBee64BitAddr;
            byte[] received = msg.Data;

            if (NodeID.Equals(rxb.NodeID))
            {
                AddDataReceived(received);
            }
            else
            {
                //Console.WriteLine("WTF... received data from {0} but this is node {1}", rxb.NodeID, NodeID);
            }
        }

        private void HandleXBeePacketReceived(Object sender, XBeeLibrary.Core.Events.PacketReceivedEventArgs args)
        {
            if (args.ReceivedPacket is TransmitStatusPacket)
            {
                TransmitStatusPacket receivedPacket = (TransmitStatusPacket)args.ReceivedPacket;
                if(receivedPacket.TransmitStatus != XBeeTransmitStatus.SUCCESS)
                {
                    Console.WriteLine("!!!!!!! Transmit issues: {0}", NodeID);
                }
            }
        }

        public void Open()
        {
            if (IsOpen) return;

            if (Monitor.TryEnter(XBCoordinatorLock, COORDINATOR_LOCK_TIMEOUT))
            {
                try
                {
                    if (!XBCoordinator.IsOpen)
                    {
                        Console.WriteLine("Opening coordinator while opening connection to {0}", NodeID);
                        XBCoordinator.Open();
                    }

                    XBeeNetwork network = XBCoordinator.GetNetwork();
                    if (network == null)
                    {
                        throw new NetworkNotFoundException(String.Format("Open: Cannot find coordinator network when looking for NodeID {0}", NodeID));
                    }
                    XBRemoteDevice = network.DiscoverDevice(NodeID);
                    if (XBRemoteDevice == null)
                    {
                        throw new BoardNotFoundException(String.Format("Open: Cannot find XBee with NodeID {0}", NodeID));
                    }

                    //delay for a while ...
                    System.Threading.Thread.Sleep(DELAY_AFTER_CONNECT);

                    XBCoordinator.DataReceived += HandleXBeeDataReceived;
                    XBCoordinator.PacketReceived += HandleXBeePacketReceived;
                } finally
                {
                    Monitor.Exit(XBCoordinatorLock);
                }

            } else
            {
                throw new TimeoutException(String.Format("Open: could not obatin lock when trying to open NodeID {0}", NodeID));
            }
            IsOpen = true;

            //start up the thread that waits for received data and forwards it to subscribers
            _deliverDataTask = Task.Run(() => DeliverData());
        }

        public void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;
            if (!_deliverDataTask.IsCompleted)
            {
                _deliverDataTask.Wait();
            }
            FlushBuffer();

            if (Monitor.TryEnter(XBCoordinatorLock, COORDINATOR_LOCK_TIMEOUT))
            {
                try
                {
                    XBCoordinator.DataReceived -= HandleXBeeDataReceived;
                    XBCoordinator.PacketReceived -= HandleXBeePacketReceived;
                    XBRemoteDevice = null;

                    bool allClosed = true;
                    foreach (var cnn in _connections[PortName])
                    {
                        if (cnn.IsOpen)
                        {
                            allClosed = false;
                            break;
                        }
                    }

                    if (allClosed && XBCoordinator.IsOpen)
                    {
                        XBCoordinator.Close();
                        Console.WriteLine("{0} closing coordinator", NodeID);

                    }
                }
                finally
                {
                    Monitor.Exit(XBCoordinatorLock);
                }
            } else
            {
                throw new TimeoutException(String.Format("Close: could not obatin lock when trying to close NodeID {0}", NodeID));
            }
        }


        public int ReadByte()
        {
            if (BytesToRead > 0)
            {
                int b = (int)DataBuffer[_bufferReadPosition];
                lock (bufferPositionLock)
                {
                    _bufferReadPosition = (_bufferReadPosition + 1) % BUFFER_SIZE;
                }
                return b;
            }
            else
            {
                return -1;
            }
        }

        public void Write(String text)
        {
            //_serialPort.Write(text);
            Console.WriteLine("oops... write string");
            throw new NotImplementedException("XBeeFirmataSerialConnection::Write string");
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (offset == 0)
            {
                if (XBRemoteDevice == null)
                {
                    throw new BoardNotFoundException("XBeeFirmataSerialConnection::Write ... cannot send data as no remote XBee device available");
                }

                if (Monitor.TryEnter(XBCoordinatorLock, COORDINATOR_LOCK_TIMEOUT))
                {
                    try
                    {
                        //XBCoordinator.SendDataAsync(XBRemoteDevice, buffer);
                        XBCoordinator.SendData(XBRemoteDevice, buffer);
                    }
                    finally
                    {
                        Monitor.Exit(XBCoordinatorLock);
                    }
                } else
                {
                    //TODO: couldn't obain a lock.. for now we just let it pass
                }
            }
            else
            {
                throw new NotImplementedException("XBeeFirmataSerialConnection::Write .. cannot have a non-zero offset");
            }
        }

        public void Write(char[] buffer, int offset, int count)
        {
            Console.WriteLine("oops... write char buffer");
            throw new NotImplementedException("XBeeFirmataSericalConnection::Write char[]");
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
