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
        public const uint FILE_BEGIN = 0;

        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;

        public const uint IOCTL_DISK_DELETE_DRIVE_LAYOUT = 0x0007C100;
        public const uint IOCTL_DISK_CREATE_DISK = 0x0007C058;
        public const uint IOCTL_DISK_GET_DRIVE_GEOMETRY = 0x00070000;
        public const uint IOCTL_DISK_GET_LENGTH_INFO = 0x7405C;
        public const uint IOCTL_DISK_UPDATE_DRIVE_SIZE = 0x0007C0C0;
        public const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x00070140;

        public const uint IOCTL_VOLUME_ONLINE = 0x0056C008;

        public const uint IOCTL_STORAGE_EJECT_MEDIA = 0x2D4808;
        public const uint IOCTL_STORAGE_LOAD_MEDIA = 0x2D4804;

        public const uint FSCTL_LOCK_VOLUME = 0x00090018;
        public const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;

        public const uint FILE_VOLUME_IS_SYSTEM = 0x00002000;
        public const uint FILE_SYSTEM_IS_HIDDEN = 0x00010000;

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
        public struct GET_LENGTH_INFORMATION
        {
            public long Length;
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
        public static extern bool FlushFileBuffers(SafeFileHandle hFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetFilePointerEx(SafeFileHandle hFile, long liDistanceToMove, out long lpNewFilePointer, uint dwMoveMethod);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle hFile, IntPtr lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

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
    }
}