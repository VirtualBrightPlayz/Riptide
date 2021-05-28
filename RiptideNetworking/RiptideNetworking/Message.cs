﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace RiptideNetworking
{
    /// <summary>The send mode of a message.</summary>
    public enum MessageSendMode : byte
    {
        /// <summary>Unreliable send mode.</summary>
        unreliable = HeaderType.unreliable,
        /// <summary>Reliable send mode.</summary>
        reliable = HeaderType.reliable,
    }

    /// <summary>The header type of a message.</summary>
    internal enum HeaderType : byte
    {
        /// <summary>For unreliable user messages.</summary>
        unreliable,
        /// <summary>For unreliable internal ack messages.</summary>
        ack,
        /// <summary>For unreliable internal ack messages (when acknowledging a sequence ID other than the last received one).</summary>
        ackExtra,
        /// <summary>For unreliable internal connect messages.</summary>
        connect,
        /// <summary>For unreliable internal heartbeat messages.</summary>
        heartbeat,
        /// <summary>For unreliable internal disconnect messages.</summary>
        disconnect,
        /// <summary>For reliable user messages.</summary>
        reliable,
        /// <summary>For reliable internal welcome messages.</summary>
        welcome,
        /// <summary>For reliable internal client connected messages.</summary>
        clientConnected,
        /// <summary>For reliable internal client disconnected messages.</summary>
        clientDisconnected,
    }

    /// <summary>Represents a packet.</summary>
    public class Message
    {
        /// <summary>The message instance used for sending user messages.</summary>
        private static readonly Message send = new Message();
        /// <summary>The message instance used for sending internal messages.</summary>
        private static readonly Message sendInternal = new Message();
        /// <summary>The message instance used for handling user messages.</summary>
        private static readonly Message handle = new Message();
        /// <summary>The message instance used for handling internal messages.</summary>
        private static readonly Message handleInternal = new Message();

        /// <summary>How many bytes a bool is represented by.</summary>
        public const byte boolLength = sizeof(bool);
        /// <summary>How many bytes a short (or ushort) is represented by.</summary>
        public const byte shortLength = sizeof(short);
        /// <summary>How many bytes an int (or uint) is represented by.</summary>
        public const byte intLength = sizeof(int);
        /// <summary>How many bytes a long (or ulong) is represented by.</summary>
        public const byte longLength = sizeof(long);
        /// <summary>How many bytes a float is represented by.</summary>
        public const byte floatLength = sizeof(float);
        /// <summary>How many bytes a double is represented by.</summary>
        public const byte doubleLength = sizeof(double);

        /// <summary>The length in bytes of the message's contents.</summary>
        public int Length { get; private set; }
        /// <summary>The length in bytes of the unread data contained in the message.</summary>
        public int UnreadLength => Length - readPos;
        /// <summary>How many more bytes can be written into the packet.</summary>
        internal int UnwrittenLength => Bytes.Length - writePos;
        /// <summary>The message's send mode.</summary>
        internal MessageSendMode SendMode { get; private set; }
        /// <summary>The message's data.</summary>
        internal byte[] Bytes { get; private set; }

        /// <summary>The position in the byte array that the next bytes will be written to.</summary>
        private ushort writePos = 0;
        /// <summary>The position in the byte array that the next bytes will be read from.</summary>
        private ushort readPos = 0;

        /// <summary>Initializes a reusable Message instance.</summary>
        /// <param name="maxSize">The maximum amount of bytes the message can contain.</param>
        private Message(ushort maxSize = 1500)
        {
            Bytes = new byte[maxSize];
        }

        /// <summary>Initializes the Message instance used for sending with new values.</summary>
        /// <param name="sendMode">The mode in which the message should be sent.</param>
        /// <param name="id">The message ID.</param>
        public static Message Create(MessageSendMode sendMode, ushort id)
        {
            Create(send, (HeaderType)sendMode);
            send.Add(id);
            return send;
        }

        /// <summary>Initializes the Message instance used for handling with new values.</summary>
        /// <param name="headerType">The message's header type.</param>
        /// <param name="data">The bytes contained in the message.</param>
        internal static Message Create(HeaderType headerType, byte[] data)
        {
            Create(handle, headerType, data);
            return handle;
        }

        /// <summary>Initializes the Message instance used for sending internal messages with new values.</summary>
        /// <param name="headerType">The message's header type.</param>
        internal static Message CreateInternal(HeaderType headerType)
        {
            Create(sendInternal, headerType);
            return sendInternal;
        }

        /// <summary>Initializes the Message instance used for handling internal messages with new values.</summary>
        /// <param name="headerType">The message's header type.</param>
        /// <param name="data">The bytes contained in the message.</param>
        internal static Message CreateInternal(HeaderType headerType, byte[] data)
        {
            Create(handleInternal, headerType, data);
            return handleInternal;
        }

        /// <summary>Initializes a Message instance for sending with new values.</summary>
        /// <param name="message">The message to initialize.</param>
        /// <param name="headerType">The message's header type.</param>
        private static void Create(Message message, HeaderType headerType)
        {
            message.SendMode = headerType >= HeaderType.reliable ? MessageSendMode.reliable : MessageSendMode.unreliable;
            message.writePos = 0;
            message.readPos = 0;
            message.Add((byte)headerType);
            if (message.SendMode == MessageSendMode.reliable)
                message.writePos += shortLength;
        }

        /// <summary>Initializes a Message instance for handling with new values.</summary>
        /// <param name="message">The message to initialize.</param>
        /// <param name="headerType">The message's header type.</param>
        /// <param name="data">The bytes contained in the message.</param>
        private static void Create(Message message, HeaderType headerType, byte[] data)
        {
            message.SendMode = headerType >= HeaderType.reliable ? MessageSendMode.reliable : MessageSendMode.unreliable;
            message.writePos = 0;
            message.readPos = (ushort)(message.SendMode == MessageSendMode.reliable ? 3 : 1);

            if (data.Length > message.Bytes.Length)
            {
                RiptideLogger.Log($"Can't fully handle {data.Length} bytes because it exceeds the maximum of {message.Bytes.Length}, message will contain incomplete data!");
                Array.Copy(data, 0, message.Bytes, 0, message.Bytes.Length);
                message.Length = message.Bytes.Length;
            }
            else
            {
                Array.Copy(data, 0, message.Bytes, 0, data.Length);
                message.Length = data.Length;
            }
        }

        #region Functions
        /// <summary>Sets the bytes reserved for the sequence ID (should only be called on reliable messages).</summary>
        /// <param name="seqId">The sequence ID to insert.</param>
        internal void SetSequenceIdBytes(ushort seqId)
        {
            byte[] sequenceIdBytes = StandardizeEndianness(BitConverter.GetBytes(seqId));
            Bytes[1] = sequenceIdBytes[0];
            Bytes[2] = sequenceIdBytes[1];
        }

        /// <summary>Standardizes byte order across big and little endian systems by reversing the given bytes on big endian systems.</summary>
        /// <param name="value">The bytes whose order to standardize.</param>
        internal static byte[] StandardizeEndianness(byte[] value)
        {
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(value);
            return value;
        }
        /// <summary>Standardizes byte order across big and little endian systems by reversing byteAmount bytes at the message's current read position on big endian systems.</summary>
        /// <param name="byteAmount">The number of bytes whose order to standardize, starting at the message's current read position.</param>
        internal void StandardizeEndianness(int byteAmount)
        {
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(Bytes, readPos, byteAmount);
        }
        #endregion

        #region Write & Read Data
        #region Byte
        /// <summary>Adds a byte to the message.</summary>
        /// <param name="value">The byte to add.</param>
        public Message Add(byte value)
        {
            if (UnwrittenLength < 1)
                throw new Exception($"Message has insufficient remaining capacity ({UnwrittenLength}) to add type 'byte'!");

            Bytes[writePos++] = value;
            return this;
        }

        /// <summary>Adds an array of bytes to the message.</summary>
        /// <param name="value">The byte array to add.</param>
        public Message Add(byte[] value)
        {
            if (UnwrittenLength < value.Length)
                throw new Exception($"Message has insufficient remaining capacity ({UnwrittenLength}) to add type 'byte[]'!");

            Array.Copy(value, 0, Bytes, writePos, value.Length);
            writePos += (ushort)value.Length;
            return this;
        }

        /// <summary>Reads a byte from the message.</summary>
        public byte GetByte()
        {
            if (UnreadLength < 1)
                throw new Exception($"Message contains insufficient unread bytes ({UnreadLength}) to read type 'byte'!");
            
            // If there are enough unread bytes
            byte value = Bytes[readPos]; // Get the byte at readPos' position
            readPos += 1;
            return value;
        }

        /// <summary>Reads an array of bytes from the message.</summary>
        /// <param name="length">The length of the byte array.</param>
        public byte[] GetByteArray(int length)
        {
            if (UnreadLength < length)
                throw new Exception($"Message contains insufficient unread bytes ({UnreadLength}) to read type 'byte[]'!");

            // If there are enough unread bytes
            byte[] value = new byte[length];
            Array.Copy(Bytes, readPos, value, 0, length); // Copy the bytes at readPos' position to the array that will be returned
            readPos += (ushort)length;
            return value;
        }
        #endregion

        #region Bool
        /// <summary>Adds a bool to the message.</summary>
        /// <param name="value">The bool to add.</param>
        public Message Add(bool value)
        {
            if (UnwrittenLength < boolLength)
                throw new Exception($"Message has insufficient remaining capacity ({UnwrittenLength}) to add type 'bool'!");

            Bytes[writePos++] = BitConverter.GetBytes(value)[0];
            return this;
        }

        /// <summary>Reads a bool from the message.</summary>
        public bool GetBool()
        {
            if (UnreadLength < boolLength)
                throw new Exception($"Message contains insufficient unread bytes ({UnreadLength}) to read type 'bool'!");
            
            // If there are enough unread bytes
            bool value = BitConverter.ToBoolean(Bytes, readPos); // Convert the bytes at readPos' position to a bool
            readPos += boolLength;
            return value;
        }

        /// <summary>Adds an array of bools to the message.</summary>
        /// <param name="array">The array to add.</param>
        /// <param name="includeLength">Whether or not to add the length of the array to the message.</param>
        public Message Add(bool[] array, bool includeLength = true)
        {
            if (includeLength)
                Add((ushort)array.Length);

            BitArray bits = new BitArray(array);
            byte[] bytes = new byte[array.Length / 8 + (array.Length % 8 == 0 ? 0 : 1)];
            bits.CopyTo(bytes, 0);
            Add(bytes);
            return this;
        }

        /// <summary>Reads an array of bools from the message.</summary>
        public bool[] GetBoolArray()
        {
            return GetBoolArray(GetUShort());
        }
        /// <summary>Reads an array of bools from the message.</summary>
        /// <param name="length">The length of the array.</param>
        public bool[] GetBoolArray(ushort length)
        {
            byte[] bytes = GetByteArray(length / 8 + (length % 8 == 0 ? 0 : 1));
            BitArray bits = new BitArray(bytes);
            bool[] array = new bool[length];
            for (int i = 0; i < array.Length; i++)
                array[i] = bits.Get(i);

            return array;
        }
        #endregion

        #region Short
        /// <summary>Adds a short to the message.</summary>
        /// <param name="value">The short to add.</param>
        public Message Add(short value)
        {
            if (UnwrittenLength < shortLength)
                throw new Exception($"Message has insufficient remaining capacity ({UnwrittenLength}) to add type 'short'!");

            byte[] valueBytes = StandardizeEndianness(BitConverter.GetBytes(value));
            Bytes[writePos++] = valueBytes[0];
            Bytes[writePos++] = valueBytes[1];
            return this;
        }

        /// <summary>Reads a short from the message.</summary>
        public short GetShort()
        {
            if (UnreadLength < shortLength)
                throw new Exception($"Message contains insufficient unread bytes ({UnreadLength}) to read type 'short'!");
            
            // If there are enough unread bytes
            StandardizeEndianness(shortLength);
            short value = BitConverter.ToInt16(Bytes, readPos); // Convert the bytes at readPos' position to a short
            readPos += shortLength;
            return value;
        }

        /// <summary>Adds an array of shorts to the message.</summary>
        /// <param name="array">The array to add.</param>
        /// <param name="includeLength">Whether or not to add the length of the array to the message.</param>
        public Message Add(short[] array, bool includeLength = true)
        {
            if (includeLength)
                Add((ushort)array.Length);

            for (int i = 0; i < array.Length; i++)
                Add(array[i]);

            return this;
        }

        /// <summary>Reads an array of shorts from the message.</summary>
        public short[] GetShortArray()
        {
            return GetShortArray(GetUShort());
        }
        /// <summary>Reads an array of shorts from the message.</summary>
        /// <param name="length">The length of the array.</param>
        public short[] GetShortArray(ushort length)
        {
            short[] array = new short[length];
            for (int i = 0; i < array.Length; i++)
                array[i] = GetShort();

            return array;
        }
        #endregion

        #region UShort
        /// <summary>Adds a ushort to the message.</summary>
        /// <param name="value">The ushort to add.</param>
        public Message Add(ushort value)
        {
            if (UnwrittenLength < shortLength)
                throw new Exception($"Message has insufficient remaining capacity ({UnwrittenLength}) to add type 'ushort'!");

            byte[] valueBytes = StandardizeEndianness(BitConverter.GetBytes(value));
            Bytes[writePos++] = valueBytes[0];
            Bytes[writePos++] = valueBytes[1];
            return this;
        }

        /// <summary>Reads a ushort from the message.</summary>
        public ushort GetUShort()
        {
            if (UnreadLength < shortLength)
                throw new Exception($"Message contains insufficient unread bytes ({UnreadLength}) to read type 'ushort'!");
            
            // If there are enough unread bytes
            StandardizeEndianness(shortLength);
            ushort value = BitConverter.ToUInt16(Bytes, readPos); // Convert the bytes at readPos' position to a ushort
            readPos += shortLength;
            return value;
        }

        /// <summary>Reads a ushort from the message without moving the read position, allowing the same bytes to be read again.</summary>
        internal ushort PeekUShort()
        {
            if (UnreadLength < shortLength)
                throw new Exception($"Message contains insufficient unread bytes ({UnreadLength}) to peek type 'ushort'!");
            
            // If there are enough unread bytes
            byte[] bytesToConvert = new byte[shortLength];
            Array.Copy(Bytes, readPos, bytesToConvert, 0, shortLength);
            return BitConverter.ToUInt16(StandardizeEndianness(bytesToConvert), 0); // Convert the bytes at readPos' position to a ushort
        }

        /// <summary>Adds an array of ushorts to the message.</summary>
        /// <param name="array">The array to add.</param>
        /// <param name="includeLength">Whether or not to add the length of the array to the message.</param>
        public Message Add(ushort[] array, bool includeLength = true)
        {
            if (includeLength)
                Add((ushort)array.Length);

            for (int i = 0; i < array.Length; i++)
                Add(array[i]);

            return this;
        }

        /// <summary>Reads an array of ushorts from the message.</summary>
        public ushort[] GetUShortArray()
        {
            return GetUShortArray(GetUShort());
        }
        /// <summary>Reads an array of ushorts from the message.</summary>
        /// <param name="length">The length of the array.</param>
        public ushort[] GetUShortArray(ushort length)
        {
            ushort[] array = new ushort[length];
            for (int i = 0; i < array.Length; i++)
                array[i] = GetUShort();
            
            return array;
        }
        #endregion

        #region Int
        /// <summary>Adds an int to the message.</summary>
        /// <param name="value">The int to add.</param>
        public Message Add(int value)
        {
            if (UnwrittenLength < intLength)
                throw new Exception($"Message has insufficient remaining capacity ({UnwrittenLength}) to add type 'int'!");

            byte[] valueBytes = StandardizeEndianness(BitConverter.GetBytes(value));
            Bytes[writePos++] = valueBytes[0];
            Bytes[writePos++] = valueBytes[1];
            Bytes[writePos++] = valueBytes[2];
            Bytes[writePos++] = valueBytes[3];
            return this;
        }

        /// <summary>Reads an int from the message.</summary>
        public int GetInt()
        {
            if (UnreadLength < intLength)
                throw new Exception($"Message contains insufficient unread bytes ({UnreadLength}) to read type 'int'!");
            
            // If there are enough unread bytes
            StandardizeEndianness(intLength);
            int value = BitConverter.ToInt32(Bytes, readPos); // Convert the bytes at readPos' position to an int
            readPos += intLength;
            return value;
        }

        /// <summary>Adds an array of ints to the message.</summary>
        /// <param name="array">The array to add.</param>
        /// <param name="includeLength">Whether or not to add the length of the array to the message.</param>
        public Message Add(int[] array, bool includeLength = true)
        {
            if (includeLength)
                Add((ushort)array.Length);

            for (int i = 0; i < array.Length; i++)
                Add(array[i]);

            return this;
        }

        /// <summary>Reads an array of ints from the message.</summary>
        public int[] GetIntArray()
        {
            return GetIntArray(GetUShort());
        }
        /// <summary>Reads an array of ints from the message.</summary>
        /// <param name="length">The length of the array.</param>
        public int[] GetIntArray(ushort length)
        {
            int[] array = new int[length];
            for (int i = 0; i < array.Length; i++)
                array[i] = GetInt();

            return array;
        }
        #endregion

        #region UInt
        /// <summary>Adds a uint to the message.</summary>
        /// <param name="value">The uint to add.</param>
        public Message Add(uint value)
        {
            if (UnwrittenLength < intLength)
                throw new Exception($"Message has insufficient remaining capacity ({UnwrittenLength}) to add type 'uint'!");

            byte[] valueBytes = StandardizeEndianness(BitConverter.GetBytes(value));
            Bytes[writePos++] = valueBytes[0];
            Bytes[writePos++] = valueBytes[1];
            Bytes[writePos++] = valueBytes[2];
            Bytes[writePos++] = valueBytes[3];
            return this;
        }

        /// <summary>Reads a uint from the message.</summary>
        public uint GetUInt()
        {
            if (UnreadLength < intLength)
                throw new Exception($"Message contains insufficient unread bytes ({UnreadLength}) to read type 'uint'!");
            
            // If there are enough unread bytes
            StandardizeEndianness(intLength);
            uint value = BitConverter.ToUInt32(Bytes, readPos); // Convert the bytes at readPos' position to an uint
            readPos += intLength;
            return value;
        }

        /// <summary>Adds an array of uints to the message.</summary>
        /// <param name="array">The array to add.</param>
        /// <param name="includeLength">Whether or not to add the length of the array to the message.</param>
        public Message Add(uint[] array, bool includeLength = true)
        {
            if (includeLength)
                Add((ushort)array.Length);

            for (int i = 0; i < array.Length; i++)
                Add(array[i]);

            return this;
        }

        /// <summary>Reads an array of uints from the message.</summary>
        public uint[] GetUIntArray()
        {
            return GetUIntArray(GetUShort());
        }
        /// <summary>Reads an array of uints from the message.</summary>
        /// <param name="length">The length of the array.</param>
        public uint[] GetUIntArray(ushort length)
        {
            uint[] array = new uint[length];
            for (int i = 0; i < array.Length; i++)
                array[i] = GetUInt();

            return array;
        }
        #endregion

        #region Long
        /// <summary>Adds a long to the message.</summary>
        /// <param name="value">The long to add.</param>
        public Message Add(long value)
        {
            if (UnwrittenLength < longLength)
                throw new Exception($"Message has insufficient remaining capacity ({UnwrittenLength}) to add type 'long'!");

            byte[] valueBytes = StandardizeEndianness(BitConverter.GetBytes(value));
            Bytes[writePos++] = valueBytes[0];
            Bytes[writePos++] = valueBytes[1];
            Bytes[writePos++] = valueBytes[2];
            Bytes[writePos++] = valueBytes[3];
            Bytes[writePos++] = valueBytes[4];
            Bytes[writePos++] = valueBytes[5];
            Bytes[writePos++] = valueBytes[6];
            Bytes[writePos++] = valueBytes[7];
            return this;
        }

        /// <summary>Reads a long from the message.</summary>
        public long GetLong()
        {
            if (UnreadLength < longLength)
                throw new Exception($"Message contains insufficient unread bytes ({UnreadLength}) to read type 'long'!");
            
            // If there are enough unread bytes
            StandardizeEndianness(longLength);
            long value = BitConverter.ToInt64(Bytes, readPos); // Convert the bytes at readPos' position to a long;
            readPos += longLength;
            return value;
        }

        /// <summary>Adds an array of longs to the message.</summary>
        /// <param name="array">The array to add.</param>
        /// <param name="includeLength">Whether or not to add the length of the array to the message.</param>
        public Message Add(long[] array, bool includeLength = true)
        {
            if (includeLength)
                Add((ushort)array.Length);

            for (int i = 0; i < array.Length; i++)
                Add(array[i]);

            return this;
        }

        /// <summary>Reads an array of longs from the message.</summary>
        public long[] GetLongArray()
        {
            return GetLongArray(GetUShort());
        }
        /// <summary>Reads an array of longs from the message.</summary>
        /// <param name="length">The length of the array.</param>
        public long[] GetLongArray(ushort length)
        {
            long[] array = new long[length];
            for (int i = 0; i < array.Length; i++)
                array[i] = GetLong();

            return array;
        }
        #endregion

        #region ULong
        /// <summary>Adds a ulong to the message.</summary>
        /// <param name="value">The ulong to add.</param>
        public Message Add(ulong value)
        {
            if (UnwrittenLength < longLength)
                throw new Exception($"Message has insufficient remaining capacity ({UnwrittenLength}) to add type 'ulong'!");

            byte[] valueBytes = StandardizeEndianness(BitConverter.GetBytes(value));
            Bytes[writePos++] = valueBytes[0];
            Bytes[writePos++] = valueBytes[1];
            Bytes[writePos++] = valueBytes[2];
            Bytes[writePos++] = valueBytes[3];
            Bytes[writePos++] = valueBytes[4];
            Bytes[writePos++] = valueBytes[5];
            Bytes[writePos++] = valueBytes[6];
            Bytes[writePos++] = valueBytes[7];
            return this;
        }

        /// <summary>Reads a ulong from the message.</summary>
        public ulong GetULong()
        {
            if (UnreadLength < longLength)
                throw new Exception($"Message contains insufficient unread bytes ({UnreadLength}) to read type 'ulong'!");
            
            // If there are enough unread bytes
            StandardizeEndianness(longLength);
            ulong value = BitConverter.ToUInt64(Bytes, readPos); // Convert the bytes at readPos' position to a ulong
            readPos += longLength;
            return value;
        }

        /// <summary>Adds an array of ulongs to the message.</summary>
        /// <param name="array">The array to add.</param>
        /// <param name="includeLength">Whether or not to add the length of the array to the message.</param>
        public Message Add(ulong[] array, bool includeLength = true)
        {
            if (includeLength)
                Add((ushort)array.Length);

            for (int i = 0; i < array.Length; i++)
                Add(array[i]);

            return this;
        }

        /// <summary>Reads an array of ulongs from the message.</summary>
        public ulong[] GetULongArray()
        {
            return GetULongArray(GetUShort());
        }
        /// <summary>Reads an array of ulongs from the message.</summary>
        /// <param name="length">The length of the array.</param>
        public ulong[] GetULongArray(ushort length)
        {
            ulong[] array = new ulong[length];
            for (int i = 0; i < array.Length; i++)
                array[i] = GetULong();

            return array;
        }
        #endregion

        #region Float
        /// <summary>Adds a float to the message.</summary>
        /// <param name="value">The float to add.</param>
        public Message Add(float value)
        {
            if (UnwrittenLength < floatLength)
                throw new Exception($"Message has insufficient remaining capacity ({UnwrittenLength}) to add type 'float'!");

            byte[] valueBytes = StandardizeEndianness(BitConverter.GetBytes(value));
            Bytes[writePos++] = valueBytes[0];
            Bytes[writePos++] = valueBytes[1];
            Bytes[writePos++] = valueBytes[2];
            Bytes[writePos++] = valueBytes[3];
            return this;
        }

        /// <summary>Reads a float from the message.</summary>
        public float GetFloat()
        {
            if (UnreadLength < floatLength)
                throw new Exception($"Message contains insufficient unread bytes ({UnreadLength}) to read type 'float'!");
            
            // If there are enough unread bytes
            StandardizeEndianness(floatLength);
            float value = BitConverter.ToSingle(Bytes, readPos); // Convert the bytes at readPos' position to a float
            readPos += floatLength;
            return value;
        }

        /// <summary>Adds an array of floats to the message.</summary>
        /// <param name="array">The array to add.</param>
        /// <param name="includeLength">Whether or not to add the length of the array to the message.</param>
        public Message Add(float[] array, bool includeLength = true)
        {
            if (includeLength)
                Add((ushort)array.Length);

            for (int i = 0; i < array.Length; i++)
                Add(array[i]);

            return this;
        }

        /// <summary>Reads an array of floats from the message.</summary>
        public float[] GetFloatArray()
        {
            return GetFloatArray(GetUShort());
        }
        /// <summary>Reads an array of floats from the message.</summary>
        /// <param name="length">The length of the array.</param>
        public float[] GetFloatArray(ushort length)
        {
            float[] array = new float[length];
            for (int i = 0; i < array.Length; i++)
                array[i] = GetFloat();

            return array;
        }
        #endregion

        #region Double
        /// <summary>Adds a double to the message.</summary>
        /// <param name="value">The double to add.</param>
        public Message Add(double value)
        {
            if (UnwrittenLength < doubleLength)
                throw new Exception($"Message has insufficient remaining capacity ({UnwrittenLength}) to add type 'double'!");

            byte[] valueBytes = StandardizeEndianness(BitConverter.GetBytes(value));
            Bytes[writePos++] = valueBytes[0];
            Bytes[writePos++] = valueBytes[1];
            Bytes[writePos++] = valueBytes[2];
            Bytes[writePos++] = valueBytes[3];
            Bytes[writePos++] = valueBytes[4];
            Bytes[writePos++] = valueBytes[5];
            Bytes[writePos++] = valueBytes[6];
            Bytes[writePos++] = valueBytes[7];
            return this;
        }

        /// <summary>Reads a double from the message.</summary>
        public double GetDouble()
        {
            if (UnreadLength < doubleLength)
                throw new Exception($"Message contains insufficient unread bytes ({UnreadLength}) to read type 'double'!");
            
            // If there are enough unread bytes
            StandardizeEndianness(doubleLength);
            double value = BitConverter.ToDouble(Bytes, readPos); // Convert the bytes at readPos' position to a double
            readPos += doubleLength;
            return value;
        }

        /// <summary>Adds an array of doubles to the message.</summary>
        /// <param name="array">The array to add.</param>
        /// <param name="includeLength">Whether or not to add the length of the array to the message.</param>
        public Message Add(double[] array, bool includeLength = true)
        {
            if (includeLength)
                Add((ushort)array.Length);

            for (int i = 0; i < array.Length; i++)
                Add(array[i]);

            return this;
        }

        /// <summary>Reads an array of doubles from the message.</summary>
        public double[] GetDoubleArray()
        {
            return GetDoubleArray(GetUShort());
        }
        /// <summary>Reads an array of doubles from the message.</summary>
        /// <param name="length">The length of the array.</param>
        public double[] GetDoubleArray(ushort length)
        {
            double[] array = new double[length];
            for (int i = 0; i < array.Length; i++)
                array[i] = GetDouble();

            return array;
        }
        #endregion

        #region String
        /// <summary>Adds a string to the message.</summary>
        /// <param name="value">The string to add.</param>
        public Message Add(string value)
        {
            byte[] stringBytes = Encoding.UTF8.GetBytes(value);
            Add((ushort)stringBytes.Length); // Add the length of the string (in bytes) to the message

            if (UnwrittenLength < stringBytes.Length)
                throw new Exception($"Message has insufficient remaining capacity ({UnwrittenLength}) to add type 'string'!");

            Add(stringBytes); // Add the string itself
            return this;
        }

        /// <summary>Reads a string from the message.</summary>
        public string GetString()
        {
            ushort length = GetUShort(); // Get the length of the string (in bytes)
            if (UnreadLength < length)
                throw new Exception($"Message contains insufficient unread bytes ({UnreadLength}) to read type 'string'!");
            
            string value = Encoding.UTF8.GetString(Bytes, readPos, length); // Convert the bytes at readPos' position to a string
            readPos += length;
            return value;
        }

        /// <summary>Adds an array of strings to the message.</summary>
        /// <param name="array">The array to add.</param>
        /// <param name="includeLength">Whether or not to add the length of the array to the message.</param>
        public Message Add(string[] array, bool includeLength = true)
        {
            if (includeLength)
                Add((ushort)array.Length);

            for (int i = 0; i < array.Length; i++)
                Add(array[i]);

            return this;
        }

        /// <summary>Reads an array of strings from the message.</summary>
        public string[] GetStringArray()
        {
            return GetStringArray(GetUShort());
        }
        /// <summary>Reads an array of strings from the message.</summary>
        /// <param name="length">The length of the array.</param>
        public string[] GetStringArray(ushort length)
        {
            string[] array = new string[length];
            for (int i = 0; i < array.Length; i++)
                array[i] = GetString();

            return array;
        }
        #endregion
        #endregion
    }
}
