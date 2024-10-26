using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CycleGUI
{
    //todo: optimize.
    public class CB
    {
        private byte[] byteBuffer;
        public byte[] Buffer => byteBuffer;

        private int currentPosition;
        public int Len => currentPosition;

        public CB(int expectedSize = 0)
        {
            byteBuffer = new byte[Math.Max(expectedSize, 4)];
            currentPosition = 0;
        }

        public CB(byte[] init)
        {
            byteBuffer = new byte[init.Length];
            init.CopyTo(byteBuffer, 0);
            currentPosition = init.Length;
        }

        public unsafe CB Append<T>(T value) where T:unmanaged
        {
            EnsureCapacity(sizeof(T));
            MemoryMarshal.Write(new Span<byte>(byteBuffer, currentPosition, sizeof(T)), ref value);
            currentPosition += sizeof(T);
            return this;
        }

        public CB Append(string value)
        {
            if (value == null) { // treat null as empty.
                Append(1);
                Append((byte)1);
                return this;
            }

            int byteCount = Encoding.UTF8.GetByteCount(value) + 1; //zero-trailing.
            Append(byteCount);
            EnsureCapacity(byteCount);

            //.net standard 2.0
            int bytesWritten = Encoding.UTF8.GetBytes(value, 0, value.Length, byteBuffer, currentPosition);
            byteBuffer[currentPosition + bytesWritten] = 0;

            //.net standard 2.1s
            // Encoding.UTF8.GetBytes(value.AsSpan(), new Span<byte>(byteBuffer, currentPosition, byteCount - 1));
            // byteBuffer[currentPosition + byteCount - 1] = 0;
            currentPosition += byteCount;
            return this;
        }

        public CB Append(byte[] bytes)
        {
            Append(bytes.AsSpan());
            return this;
        }

        public unsafe CB Append<T>(Span<T> span) where T : unmanaged
        {
            int byteCount = span.Length * sizeof(T);
            EnsureCapacity(byteCount);
            MemoryMarshal.AsBytes(span).CopyTo(new Span<byte>(byteBuffer, currentPosition, byteCount));
            currentPosition += byteCount;
            return this;
        }

        public unsafe CB Append<T>(Memory<T> memory) where T : unmanaged
        {
            int byteCount = memory.Length * sizeof(T);
            EnsureCapacity(byteCount);
            MemoryMarshal.AsBytes(memory.Span).CopyTo(new Span<byte>(byteBuffer, currentPosition, byteCount));
            currentPosition += byteCount;
            return this;
        }

        public int Sz => currentPosition;

        public Span<byte> AsSpan()
        {
            return byteBuffer.AsSpan(0, currentPosition);
        }
        public Memory<byte> AsMemory()
        {
            return byteBuffer.AsMemory(0, currentPosition);
        }

        private void EnsureCapacity(int additionalBytes)
        {
            int requiredSize = currentPosition + additionalBytes;
            if (requiredSize <= byteBuffer.Length) return;
            int newSize = requiredSize * 2;
            Array.Resize(ref byteBuffer, newSize);
        }
    }
}
