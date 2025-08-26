namespace PickPack.Disk
{
    public class ProgressEventArgs : EventArgs
    {
        public int Percent { get; set; }

        public string ?Message1 { get; set; }

        public string ?Message2 { get; set; }
    }

    public class ImageWriterEventArgs : EventArgs
    {
        public Microsoft.Win32.SafeHandles.SafeFileHandle Handle { get; set; }
    }
}