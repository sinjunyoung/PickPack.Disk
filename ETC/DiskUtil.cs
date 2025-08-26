using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PickPack.Disk
{
    public static class DiskUtil
    {    
        public static int GetSectorSize(SafeFileHandle diskHandle)
        {
            int size = Marshal.SizeOf<Win32API.DISK_GEOMETRY>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (Win32API.DeviceIoControl(diskHandle, Win32API.IOCTL_DISK_GET_DRIVE_GEOMETRY,
                                    IntPtr.Zero, 0, buffer, (uint)size, out _, IntPtr.Zero))
                {
                    Win32API.DISK_GEOMETRY geometry = Marshal.PtrToStructure<Win32API.DISK_GEOMETRY>(buffer);
                    return (int)geometry.BytesPerSector;
                }
                else
                {
                    return 512;
                }
            }
            finally
            {   
                Marshal.FreeHGlobal(buffer);
            }
        }

        public static long GetDiskLength(int physicalDriveNumber)
        {
            string physicalDrivePath = $@"\\.\PhysicalDrive{physicalDriveNumber}";
            return GetDiskLength(physicalDrivePath);
        }

        public static long GetDiskLength(string devicePath)
        {
            using (SafeFileHandle diskHandle = Win32API.CreateFile(devicePath, FileAccess.Read, FileShare.Read,
                           IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero))
            {

                if (diskHandle.IsInvalid)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                return GetDiskLength(diskHandle);
            }
        }

        public static long GetDiskLength(SafeFileHandle diskHandle)
        {
            int size = Marshal.SizeOf<Win32API.GET_LENGTH_INFORMATION>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!Win32API.DeviceIoControl(diskHandle, Win32API.IOCTL_DISK_GET_LENGTH_INFO,
                                    IntPtr.Zero, 0, buffer, (uint)size, out _, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                Win32API.GET_LENGTH_INFORMATION lengthInfo = Marshal.PtrToStructure<Win32API.GET_LENGTH_INFORMATION>(buffer);
                return lengthInfo.Length;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public static ulong GetAvailableFreeSpace(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            ulong freeBytesAvailable = 0;
            ulong totalNumberOfBytes = 0;
            ulong totalNumberOfFreeBytes = 0;

            bool success = Win32API.GetDiskFreeSpaceEx(path, out freeBytesAvailable, out totalNumberOfBytes, out totalNumberOfFreeBytes);

            if (!success)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "디스크 용량 확인에 실패했습니다.");

            return freeBytesAvailable;
        }
    }
}
