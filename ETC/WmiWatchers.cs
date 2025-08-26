using PickPack.Disk;
using System.Diagnostics;
using System.Management;

public class WmiWatchers
{

    #region Field

    readonly ManagementEventWatcher arrivalWatcher;
    readonly ManagementEventWatcher removalWatcher;
    readonly object lockObject = new object();
    DateTime lastDriveCheckTime = DateTime.MinValue;

    #endregion

    #region #region Event and Delegate

    public event EventHandler USBArrival;
    public event EventHandler USBRemoval;

    protected virtual void OnUSBArrival()
    {
        USBArrival?.Invoke(this, new EventArgs());
    }

    protected virtual void OnUSBRemoval()
    {
        USBRemoval?.Invoke(this, new EventArgs());
    }

    #endregion

    public WmiWatchers()
    {
        WqlEventQuery arrivalQuery = new WqlEventQuery(
            "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_DiskDrive'"
        );
        this.arrivalWatcher = new ManagementEventWatcher(arrivalQuery);
        this.arrivalWatcher.EventArrived += (sender, e) =>
        {
            try
            {
                if (HasRemovableDrivesChanged())
                    OnUSBArrival();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WMI Arrival Watcher Error: {ex.Message}");
            }
        };

        this.arrivalWatcher.Start();

        WqlEventQuery removalQuery = new WqlEventQuery(
            "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_DiskDrive'"
        );
        this.removalWatcher = new ManagementEventWatcher(removalQuery);
        this.removalWatcher.EventArrived += (sender, e) =>
        {
            try
            {
                if (HasRemovableDrivesChanged())
                    OnUSBRemoval();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WMI Removal Watcher Error: {ex.Message}");
            }
        };

        this.removalWatcher.Start();
    }

    private bool HasRemovableDrivesChanged()
    {
        lock (lockObject)
        {
            try
            {
                if ((DateTime.Now - lastDriveCheckTime).TotalSeconds < 1)
                    return false;

                var existingItems = DriveInfos.Infos.ToArray();
                var newItems = DriveInfos.GetDriveInfos();

                lastDriveCheckTime = DateTime.Now;

                if (existingItems.Length != newItems.Count)
                {
                    DriveInfos.Infos.Clear();
                    DriveInfos.Infos.AddRange(newItems);

                    return true;
                }

                var existingDevices = existingItems.Select(x => new { x.DeviceId, x.Model }).OrderBy(x => x.DeviceId).ToList();
                var newDevices = newItems.Select(x => new { x.DeviceId, x.Model }).OrderBy(x => x.DeviceId).ToList();

                for (int i = 0; i < existingDevices.Count; i++)
                {
                    if (existingDevices[i].DeviceId != newDevices[i].DeviceId || existingDevices[i].Model != newDevices[i].Model)
                    {
                        DriveInfos.Infos.Clear();
                        DriveInfos.Infos.AddRange(newItems);

                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HasRemovableDrivesChanged Error: {ex.Message}");

                return false;
            }
        }
    }
}