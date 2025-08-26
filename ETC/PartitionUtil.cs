using Microsoft.Win32.SafeHandles;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace PickPack.Disk
{
    public static class PartitionUtil
    {
        static readonly string[] SafeFileSystems = { "FAT32", "NTFS", "exFAT" };

        #region Private

        private static async Task<bool> RunPowerShellScriptAsync(string script)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            try
            {
                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PowerShell 스크립트 실행 실패: {ex.Message}");
            }
            return false;
        }

        private static int GetPartitionCountByDiskNumber(int diskNumber)
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_DiskPartition WHERE DiskIndex = {diskNumber}");
            return searcher.Get().Count;
        }

        private static List<string> GetDriveLettersByDiskNumber(int diskNumber)
        {
            var driveLetters = new List<string>();
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");

            foreach (ManagementObject disk in searcher.Get())
            {
                if (Convert.ToInt32(disk["Index"]) == diskNumber)
                {
                    foreach (ManagementObject partition in disk.GetRelated("Win32_DiskPartition"))
                    {
                        foreach (ManagementObject logical in partition.GetRelated("Win32_LogicalDisk"))
                        {
                            string? deviceId = logical["DeviceID"]?.ToString();

                            if (!string.IsNullOrEmpty(deviceId))
                                driveLetters.Add(deviceId);
                        }
                    }
                    break;
                }
            }

            return driveLetters;
        }



        private static List<string> FindUnassignedVolumeGuids()
        {
            var result = new List<string>();
            var volumeName = new StringBuilder(260);
            IntPtr findHandle = Win32API.FindFirstVolume(volumeName, (uint)volumeName.Capacity);

            if (findHandle == IntPtr.Zero)
                return result;

            try
            {
                do
                {
                    string currentVolumeGuid = volumeName.ToString();

                    Win32API.GetVolumePathNamesForVolumeName(currentVolumeGuid, null, 0, out uint pathLength);
                    if (pathLength != 2) continue;

                    uint driveType = Win32API.GetDriveType(currentVolumeGuid);
                    if (driveType != 2) continue; // 2 = Removable Media

                    var fsName = new StringBuilder(260);
                    if (!Win32API.GetVolumeInformation(currentVolumeGuid, null, 0, out _, out _,
                        out uint fileSystemFlags, fsName, (uint)fsName.Capacity)) continue;

                    string fileSystem = fsName.ToString();

                    if (SafeFileSystems.Contains(fileSystem, StringComparer.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(fileSystem) &&
                        (fileSystemFlags & Win32API.FILE_SYSTEM_IS_HIDDEN) == 0 &&
                        (fileSystemFlags & Win32API.FILE_VOLUME_IS_SYSTEM) == 0)
                    {
                        result.Add(currentVolumeGuid);
                    }

                } while (Win32API.FindNextVolume(findHandle, volumeName, (uint)volumeName.Capacity));
            }
            finally
            {
                Win32API.FindVolumeClose(findHandle);
            }

            return result;
        }

        private static bool AssignDriveLetter(string volumeGuid, char? driveLetter)
        {
            if (driveLetter == null)
                return false;

            string mountPoint = $"{driveLetter}:\\";
            return Win32API.SetVolumeMountPoint(mountPoint, volumeGuid);
        }

        private static char? FindFirstAvailableDriveLetter()
        {
            var usedLetters = new HashSet<char>(
                DriveInfo.GetDrives()
                .Where(d => d.Name.Length >= 2 && char.IsLetter(d.Name[0]))
                .Select(d => d.Name[0])
                .Select(char.ToUpper)
            );

            for (char letter = 'C'; letter <= 'Z'; letter++)
            {
                if (!usedLetters.Contains(letter))
                    return letter;
            }

            return null;
        }

        #endregion

        public static async Task DeleteAllPartitionsAsync(int diskNumber)
        {
            int count = GetPartitionCountByDiskNumber(diskNumber);

            if (count > 0)
            {
                var letters = GetDriveLettersByDiskNumber(diskNumber);

                foreach (var letter in letters)
                    Win32API.DeleteVolumeMountPoint($"{letter}\\");

                for (int i = 1; i <= count; i++)
                    await RunPowerShellScriptAsync($"Remove-Partition -DiskNumber {diskNumber} -PartitionNumber {i} -Confirm:$false");
            }

            await Task.Delay(500);
        }

        public static async Task RescanDisksAsync()
        {
            await RunPowerShellScriptAsync("Update-HostStorageCache");
            await Task.Delay(500);
        }

        public static void AssignNextAvailableDriveLetter()
        {
            var guids = FindUnassignedVolumeGuids();

            for (int i = 0; i < guids.Count; i++)
                AssignDriveLetter(guids[i], FindFirstAvailableDriveLetter());
        }
    }
}