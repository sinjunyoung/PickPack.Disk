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
            ////의미가 없어 512KB로 고정
            //return 512 * 1024;

            try
            {
                var availableMemory = GC.GetTotalMemory(false);
                if (availableMemory > 16L * 1024 * 1024 * 1024)
                    return 4 * 1024 * 1024;  // 8MB → 4MB
                else if (availableMemory > 8L * 1024 * 1024 * 1024)
                    return 2 * 1024 * 1024;  // 4MB → 2MB
                else if (availableMemory > 4L * 1024 * 1024 * 1024)
                    return 1024 * 1024;      // 2MB → 1MB
                else
                    return 512 * 1024;       // 1MB → 512KB
            }
            catch
            {
                return 512 * 1024;          // 1MB → 512KB
            }
        }

        private static int GetOptimalChannelCapacity()
        {
            ////의미가 없어 1로 줄임
            //return 1;

            try
            {
                int bufferSizeMB = Optimal.BufferSize / (512 * 1024);
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
