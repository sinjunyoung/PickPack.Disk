using Microsoft.Win32.SafeHandles;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PickPack.Disk
{
    public class DriveInfos
    {
        #region Field

        internal static List<DriveInfos> Infos = new List<DriveInfos>();

        #endregion

        #region Property

        public string DevicePath { get; set; }

        public string? DriveLetter
        { 
            get
            {
                return GetDriveLetterFromDiskNumber(this.DiskNumber);
            }
        }

        public string Model { get; set; }

        public string DeviceId { get; set; }

        public long SizeBytes { get; set; }

        public int DiskNumber { get; set; }

        #endregion

        public DriveInfos(string devicePath, string model, long sizeBytes)
        {
            DevicePath = devicePath;
            Model = model;
            SizeBytes = sizeBytes;
            DiskNumber = int.Parse(Regex.Match(devicePath, @"\d+$").Value); ;
        }

        #region Override

        public override string ToString()
        {
            return $"({FileSize.FormatSize((long)SizeBytes)}) {Model}";
        }

        #endregion

        #region Public

        public static string? GetDriveLetterFromDiskNumber(int diskNumber)
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (ManagementObject disk in searcher.Get())
            {
                if (Convert.ToInt32(disk["Index"]) == diskNumber)
                {
                    foreach (ManagementObject partition in disk.GetRelated("Win32_DiskPartition"))
                    {
                        foreach (ManagementObject logical in partition.GetRelated("Win32_LogicalDisk"))
                        {
                            return logical["DeviceID"]?.ToString();
                        }
                    }
                }
            }
            return null;
        }

        public static List<DriveInfos> GetDriveInfos()
        {
            var infos = new List<DriveInfos>();

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                foreach (ManagementObject wmi_drive in searcher.Get())
                {
                    using (wmi_drive) 
                    {
                        string? deviceId = wmi_drive["DeviceID"]?.ToString();
                        string? model = wmi_drive["Model"]?.ToString();
                        string? mediaType = wmi_drive["MediaType"]?.ToString();

                        if (mediaType?.Equals("Removable Media", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            if (deviceId != null && model != null)
                            {
                                long sizeBytes = DiskUtil.GetDiskLength(deviceId);
                                if (sizeBytes > 0)
                                {
                                    infos.Add(new DriveInfos(deviceId, model, sizeBytes));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }

            return infos;
        }

        #endregion
    }
}