using System;
using System.Collections.Generic;
using System.Text;

namespace CycleGUI
{
    public class CB
    {
        private List<byte> byteList;

        public CB()
        {
            byteList = new List<byte>();
        }
        public CB(byte[] init)
        {
            byteList = new List<byte>();
            byteList.AddRange(init);
        }

        public CB Append(bool value)
        {
            Append(BitConverter.GetBytes(value));
            return this;
        }

        public CB Append(char value)
        {
            Append(BitConverter.GetBytes(value));
            return this;
        }
        public CB Append(byte value)
        {
            Append(BitConverter.GetBytes(value));
            return this;
        }

        public CB Append(short value)
        {
            Append(BitConverter.GetBytes(value));
            return this;
        }
        public CB Append(ushort value)
        {
            Append(BitConverter.GetBytes(value));
            return this;
        }

        public CB Append(int value)
        {
            Append(BitConverter.GetBytes(value));
            return this;
        }
        public CB Append(uint value)
        {
            Append(BitConverter.GetBytes(value));
            return this;
        }

        public CB Append(long value)
        {
            Append(BitConverter.GetBytes(value));
            return this;
        }

        public CB Append(ulong value)
        {
            Append(BitConverter.GetBytes(value));
            return this;
        }

        public CB Append(float value)
        {
            Append(BitConverter.GetBytes(value));
            return this;
        }

        public CB Append(double value)
        {
            Append(BitConverter.GetBytes(value));
            return this;
        }

        public CB Append(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Append(BitConverter.GetBytes(bytes.Length));
            Append(bytes);
            return this;
        }

        public CB Append(byte[] bytes)
        {
            byteList.AddRange(bytes);
            return this;
        }

        public int Sz => byteList.Count;

        public byte[] ToArray()
        {
            return byteList.ToArray();
        }
    }
}
