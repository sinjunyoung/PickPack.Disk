using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PickPack.Disk
{
    internal static class Optimal
    {
        public static readonly byte[] ZeroBuffer;
        public static readonly ArrayPool<byte> ArrayPool = ArrayPool<byte>.Shared;
        public static readonly int BufferSize;
        public static readonly int ChannelCapacity;

        static Optimal()
        {
            BufferSize = GetOptimalBufferSize();
            ChannelCapacity = GetOptimalChannelCapacity();
            ZeroBuffer = new byte[BufferSize];
        }

        private static int GetOptimalBufferSize()
        {
            try
            {
                var availableMemory = GC.GetTotalMemory(false);

                if (availableMemory > 16L * 1024 * 1024 * 1024)
                    return 8 * 1024 * 1024;
                else if (availableMemory > 8L * 1024 * 1024 * 1024)
                    return 4 * 1024 * 1024;
                else if (availableMemory > 4L * 1024 * 1024 * 1024)
                    return 2 * 1024 * 1024;
                else
                    return 1024 * 1024;
            }
            catch
            {
                return 1024 * 1024;
            }
        }

        private static int GetOptimalChannelCapacity()
        {
            try
            {
                int bufferSizeMB = Optimal.BufferSize / (1024 * 1024);
                int optimalCapacity = Math.Max(4, Math.Min(16, 64 / bufferSizeMB));
                return optimalCapacity;
            }
            catch
            {
                return 8;
            }
        }
    }
}
