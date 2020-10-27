using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XBeeLibrary.Core.Connection;
using Chetch.Utilities;
using System.IO.Ports;
using System.Threading;

namespace Chetch.Arduino.XBee
{
    /// <summary>
    /// Class that provides a Chetch.Utilities.SerialPorts.SerialPort connection for XBee devices to use
    /// </summary>
    public class XBeeSerialConnection : IConnectionInterface, IDisposable
    {
        public bool IsOpen { get => _serialPort.IsOpen; }

        public DataStream Stream { get; } = new DataStream();


        private SerialPorts.SerialPort _serialPort;

        public int BaudRate { get => _serialPort.BaudRate; set => throw new Exception("XBeeSerialConnection: cannot assign baud rate"); }
        public string PortName { get => _serialPort.PortName; set => throw new Exception("XBeeSerialConnection: cannot assign portname"); }
        public string NewLine { get => _serialPort.NewLine; set => _serialPort.NewLine = value; }

        public XBeeSerialConnection(String port, int baud)
        {
            _serialPort = new SerialPorts.SerialPort(port, baud);
        }

        public void Close()
        {
            // Do nothing if the device is not open.
            if (!IsOpen)
                return;

            _serialPort.Close();
        }

        public ConnectionType GetConnectionType()
        {
            return ConnectionType.SERIAL;
        }

        public void Open()
        {
            if (IsOpen)
                return;

            _serialPort.Open();
            _serialPort.DataReceived += HandleDataReceived;
        }

        private void HandleDataReceived(Object sender, SerialDataReceivedEventArgs eargs)
        {
            if (sender is SerialPorts.SerialPort)
            {
                int n = _serialPort.BytesToRead;
                if (n > 0)
                {
                    byte[] bytes = new byte[n];
                    _serialPort.Read(bytes, 0, n);
                    Stream.Write(bytes, 0, n);

                    lock (this)
                    {
                        Monitor.Pulse(this);
                    }
                }
            }
        }

        public int ReadData(byte[] data)
        {
            return ReadData(data, 0, data.Length);
        }

        public int ReadData(byte[] data, int offset, int length)
        {
            //Read data from the underlying data stream which has been supplied data by the serial port on the DataReceived event
            int readBytes = 0;
            if (Stream != null)
                readBytes = Stream.Read(data, offset, length);
            return readBytes;
        }

        public void SetEncryptionKeys(byte[] key, byte[] txNonce, byte[] rxNonce)
        {
            throw new NotImplementedException();
        }

        public void WriteData(byte[] data)
        {
            WriteData(data, 0, data.Length);
        }

        public void WriteData(byte[] data, int offset, int length)
        {
            //this writes data to the serial port
            _serialPort.Write(data, 0, length);
        }

        public void Dispose()
        {
            _serialPort?.Dispose();
        }
    }
}
