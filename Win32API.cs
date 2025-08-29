using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PickPack.Disk
{
    internal static class Win32API
    {
        public const uint IOCTL_DISK_DELETE_DRIVE_LAYOUT = 0x0007C100;
        public const uint IOCTL_DISK_CREATE_DISK = 0x0007C058;
        public const uint IOCTL_DISK_GET_DRIVE_GEOMETRY = 0x00070000;
        public const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x00070140;

        public const uint FILE_VOLUME_IS_SYSTEM = 0x00002000;
        public const uint FILE_SYSTEM_IS_HIDDEN = 0x00010000;

        public const uint ES_CONTINUOUS = 0x80000000;
        public const uint ES_SYSTEM_REQUIRED = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        public struct DISK_GEOMETRY
        {
            public long Cylinders;
            public int MediaType;
            public int TracksPerCylinder;
            public int SectorsPerTrack;
            public uint BytesPerSector;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CREATE_DISK_GPT
        {
            public Guid DiskId;
            public uint MaxPartitionCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CREATE_DISK
        {
            public int PartitionStyle; // 0=MBR, 1=GPT
            public CREATE_DISK_GPT Gpt;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern SafeFileHandle CreateFile(string lpFileName, FileAccess dwDesiredAccess, FileShare dwShareMode, IntPtr lpSecurityAttributes, FileMode dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteVolumeMountPoint(string lpszVolumeMountPoint);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr FindFirstVolume([Out] StringBuilder lpszVolumeName, uint cchBufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FindNextVolume(IntPtr hFindVolume, [Out] StringBuilder lpszVolumeName, uint cchBufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FindVolumeClose(IntPtr hFindVolume);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint GetDriveType(string lpRootPathName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetVolumePathNamesForVolumeName(string lpszVolumeName, [Out] char[]? lpszVolumePathNames, uint cchBufferLength, out uint lpcchReturnLength);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool GetVolumeInformation(string lpRootPathName, StringBuilder? lpVolumeNameBuffer, uint nVolumeNameSize, out uint lpVolumeSerialNumber, out uint lpMaximumComponentLength, out uint lpFileSystemFlags, StringBuilder lpFileSystemNameBuffer, uint nFileSystemNameSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetVolumeMountPoint(string lpszVolumeMountPoint, string lpszVolumeName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetDiskFreeSpaceEx(string lpDirectoryName, out ulong lpFreeBytesAvailable, out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint SetThreadExecutionState(uint esFlags);
    }
}