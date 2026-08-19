using System;

namespace SignVR.Streaming
{
    /// <summary>
    /// Binary framing shared by the Quest sender and the desktop receiver.
    /// All multi-byte fields are unsigned little-endian values.
    /// </summary>
    public static class SpectatorViewProtocol
    {
        public const byte Version = 1;
        public const int HeaderSize = 24;
        public const int DefaultMaxDatagramBytes = 1200;
        public const int MaxFrameBytes = 8 * 1024 * 1024;
        public const int MaxChunkCount = 8192;
        public const byte HeadsetPovFlag = 1 << 0;
        public const byte FlipVerticalFlag = 1 << 1;

        private static readonly byte[] Magic = { (byte)'S', (byte)'V', (byte)'R', (byte)'1' };

        public static int CalculateChunkCount(
            int frameSize,
            int maxDatagramBytes = DefaultMaxDatagramBytes
        )
        {
            if (frameSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frameSize),
                    "A frame must contain at least one byte."
                );
            }

            int payloadSize = GetChunkPayloadSize(maxDatagramBytes);
            return checked((frameSize + payloadSize - 1) / payloadSize);
        }

        public static int GetChunkPayloadSize(int maxDatagramBytes)
        {
            if (maxDatagramBytes <= HeaderSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxDatagramBytes),
                    $"Datagrams must be larger than the {HeaderSize}-byte header."
                );
            }

            return maxDatagramBytes - HeaderSize;
        }

        public static void WriteHeader(
            byte[] destination,
            byte flags,
            uint frameId,
            ushort chunkIndex,
            ushort chunkCount,
            ushort width,
            ushort height,
            uint frameSize
        )
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destination.Length < HeaderSize)
            {
                throw new ArgumentException(
                    $"The destination must contain at least {HeaderSize} bytes.",
                    nameof(destination)
                );
            }

            if (chunkCount == 0 || chunkIndex >= chunkCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkIndex),
                    "The chunk index must be inside a non-empty frame."
                );
            }

            if (width == 0 || height == 0 || frameSize == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frameSize),
                    "Frame dimensions and byte length must be non-zero."
                );
            }

            Buffer.BlockCopy(Magic, 0, destination, 0, Magic.Length);
            destination[4] = Version;
            destination[5] = flags;
            WriteUInt16(destination, 6, HeaderSize);
            WriteUInt32(destination, 8, frameId);
            WriteUInt16(destination, 12, chunkIndex);
            WriteUInt16(destination, 14, chunkCount);
            WriteUInt16(destination, 16, width);
            WriteUInt16(destination, 18, height);
            WriteUInt32(destination, 20, frameSize);
        }

        private static void WriteUInt16(byte[] destination, int offset, int value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt32(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }
    }
}
