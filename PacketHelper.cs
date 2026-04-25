using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace MazeTD.Shared
{
    public static class PacketHelper
    {
        private const int HEADER_SIZE = 4;

        // 关键：IncludeFields=true 才能序列化字段而不只是属性
        private static readonly JsonSerializerOptions _opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            IncludeFields = true,
        };

        public static byte[] Pack<T>(PacketType type, T payload)
        {
            string json = JsonSerializer.Serialize(payload, _opts); // 加_opts
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

            int bodyLen = 1 + jsonBytes.Length;
            byte[] frame = new byte[HEADER_SIZE + bodyLen];

            frame[0] = (byte)(bodyLen >> 24);
            frame[1] = (byte)(bodyLen >> 16);
            frame[2] = (byte)(bodyLen >> 8);
            frame[3] = (byte)(bodyLen);
            frame[4] = (byte)type;

            Buffer.BlockCopy(jsonBytes, 0, frame, 5, jsonBytes.Length);
            return frame;
        }

        public static byte[] Pack(PacketType type)
        {
            byte[] frame = new byte[HEADER_SIZE + 1];
            frame[0] = 0; frame[1] = 0; frame[2] = 0; frame[3] = 1;
            frame[4] = (byte)type;
            return frame;
        }

        public class ReceiveBuffer
        {
            private readonly List<byte> _buf = new List<byte>(4096);

            public void Append(byte[] data, int offset, int count)
            {
                for (int i = offset; i < offset + count; i++)
                    _buf.Add(data[i]);
            }

            public bool TryDequeue(out PacketType type, out byte[] payload)
            {
                type = 0;
                payload = Array.Empty<byte>();

                if (_buf.Count < HEADER_SIZE) return false;

                int bodyLen = (_buf[0] << 24) | (_buf[1] << 16) | (_buf[2] << 8) | _buf[3];

                if (bodyLen <= 0 || bodyLen > 1024 * 1024)
                {
                    _buf.Clear();
                    return false;
                }

                if (_buf.Count < HEADER_SIZE + bodyLen) return false;

                type = (PacketType)_buf[HEADER_SIZE];

                int payloadLen = bodyLen - 1;
                payload = new byte[payloadLen];
                _buf.CopyTo(HEADER_SIZE + 1, payload, 0, payloadLen);
                _buf.RemoveRange(0, HEADER_SIZE + bodyLen);

                return true;
            }
        }

        public static T Deserialize<T>(byte[] payload)
        {
            string json = Encoding.UTF8.GetString(payload);
            return JsonSerializer.Deserialize<T>(json, _opts) // 加_opts
                   ?? throw new InvalidOperationException(
                       $"Deserialize<{typeof(T).Name}> returned null. JSON: {json}");
        }

        public static T? TryDeserialize<T>(byte[] payload) where T : class
        {
            try { return Deserialize<T>(payload); }
            catch { return null; }
        }
    }
}