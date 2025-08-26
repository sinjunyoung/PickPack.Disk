using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace PickPack.Disk
{
    public class ProgressReporter
    {
        #region Field

        private readonly object progressLock = new object();
        private Stopwatch? stopwatch;
        private List<Tuple<double, long>> progressHistory = new List<Tuple<double, long>>();
        private CancellationToken cancellationToken;
        private DateTime lastProgressReport = DateTime.MinValue;

        #endregion

        #region Const

        private const int HISTORY_WINDOW_SECONDS = 10;

        #endregion

        #region Event

        public event EventHandler<ProgressEventArgs>? ProgressChanged;

        protected virtual void OnProgressChanged(int percent, string message1, string? message2 = "")
        {
            ProgressChanged?.Invoke(this, new ProgressEventArgs
            {
                Percent = percent,
                Message1 = message1,
                Message2 = message2
            });
        }

        #endregion

        #region Public

        public void Initialize(CancellationToken cancellationToken)
        {
            this.cancellationToken = cancellationToken;
            this.stopwatch = Stopwatch.StartNew();
            this.progressHistory.Clear();
        }

        public void ReportProgress(long transferred, long total, string operationMessage)
        {
            lock (this.progressLock)
            {
                var now = DateTime.Now;
                if ((now - lastProgressReport).TotalMilliseconds < 100)
                    return;

                lastProgressReport = now;
                this.cancellationToken.ThrowIfCancellationRequested();

                if (this.stopwatch == null)
                    throw new InvalidOperationException("ProgressReporter가 초기화되지 않았습니다.");

                double elapsedSeconds = this.stopwatch.Elapsed.TotalSeconds;
                this.progressHistory.Add(Tuple.Create(elapsedSeconds, transferred));

                CleanupProgressHistory(elapsedSeconds);

                double speedMBps = CalculateSpeed();
                double remainingBytes = total - transferred;
                double estimatedRemainingSeconds = speedMBps > 0 ? remainingBytes / (speedMBps * 1024.0 * 1024.0) : 0;
                double percent = ((double)transferred * 100 / total);

                OnProgressChanged(
                    (int)Math.Min(percent, 100),
                    $"{operationMessage} ({percent:F1}%)",
                    Format(transferred, total, elapsedSeconds, elapsedSeconds + estimatedRemainingSeconds, speedMBps));
            }
        }

        public void ReportProgressWithInterval(long transferred, long total, string operationMessage, double intervalSeconds)
        {
            lock (this.progressLock)
            {
                if (this.stopwatch == null)
                    throw new InvalidOperationException("ProgressReporter가 초기화되지 않았습니다.");

                this.cancellationToken.ThrowIfCancellationRequested();

                double now = this.stopwatch.Elapsed.TotalSeconds;
                if (now - (lastProgressReport != DateTime.MinValue ? (now - (DateTime.Now - lastProgressReport).TotalSeconds) : 0) < intervalSeconds && transferred < total)
                    return;

                lastProgressReport = DateTime.Now;

                this.progressHistory.Add(Tuple.Create(now, transferred));
                CleanupProgressHistory(now);

                double speedMBps = CalculateSpeed();
                double remainingBytes = total - transferred;
                double estimatedRemainingSeconds = speedMBps > 0 ? remainingBytes / (speedMBps * 1024.0 * 1024.0) : 0;
                double percent = ((double)transferred * 100 / total);

                if (remainingBytes <= 0) estimatedRemainingSeconds = 0;

                OnProgressChanged(
                    (int)Math.Min(percent, 100),
                    $"{operationMessage} ({percent:F1}%)",
                    Format(transferred, total, now, now + estimatedRemainingSeconds, speedMBps));
            }
        }

        public void ReportCompletion(string completionMessage)
        {
            OnProgressChanged(100, completionMessage, null);
        }

        #endregion

        #region Private

        private double CalculateSpeed()
        {
            if (this.progressHistory.Count <= 1)
                return 0;

            var first = this.progressHistory.First();
            var last = this.progressHistory.Last();
            double timeDiff = last.Item1 - first.Item1;
            long bytesDiff = last.Item2 - first.Item2;

            return timeDiff > 0.001 ? (bytesDiff / (1024.0 * 1024.0)) / timeDiff : 0;
        }

        private void CleanupProgressHistory(double currentTime)
        {
            double windowStart = currentTime - HISTORY_WINDOW_SECONDS;

            int removeCount = 0;
            for (int i = 0; i < progressHistory.Count; i++)
            {
                if (progressHistory[i].Item1 >= windowStart)
                    break;

                removeCount++;
            }

            if (removeCount > 0)
                progressHistory.RemoveRange(0, removeCount);
        }

        public static string Format(long bytesTransferred, long totalBytesToTransfer, double elapsedSeconds, double estimatedSeconds, double speedMBps)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendFormat(@"{0} / {1} | {2:hh\:mm\:ss} / {3:hh\:mm\:ss} | {4:F1}MB/s",
                FileSize.FormatSize(bytesTransferred),
                FileSize.FormatSize(totalBytesToTransfer),
                TimeSpan.FromSeconds(elapsedSeconds),
                TimeSpan.FromSeconds(estimatedSeconds),
                speedMBps);

            return sb.ToString();
        }

        #endregion
    }
}