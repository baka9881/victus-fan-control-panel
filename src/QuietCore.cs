using System;

namespace VictusQuiet
{
    public sealed class FanSample
    {
        public readonly double ZoneC, GpuC, CpuLoad, GpuLoad;
        public readonly int Fan1, Fan2;
        public FanSample(double zone, double gpu, double cpuLoad, double gpuLoad, int fan1, int fan2)
        { ZoneC = zone; GpuC = gpu; CpuLoad = cpuLoad; GpuLoad = gpuLoad; Fan1 = fan1; Fan2 = fan2; }
        public bool Valid
        {
            get { return ZoneC >= 10 && ZoneC <= 115 && GpuC >= 10 && GpuC <= 110
                && CpuLoad >= 0 && CpuLoad <= 100 && GpuLoad >= 0 && GpuLoad <= 100
                && Fan1 >= 0 && Fan1 <= 80 && Fan2 >= 0 && Fan2 <= 80; }
        }
    }
    public interface IQuietDevice : IDisposable
    {
        FanSample Read();
        void ApplyQuiet();
        void RestoreAutomatic();
    }
    public interface IRecoveryGuard : IDisposable
    {
        bool WasRecovered { get; }
        void Begin();
        void Arm();
        void Pulse();
        void Complete();
    }
    public enum CoolingMode { Automatic, Quiet, RecoveryFailed }

    public sealed class QuietSession
    {
        readonly IQuietDevice device;
        readonly IRecoveryGuard recovery;
        readonly Func<double> seconds;
        double began, lastWrite, busySince = -1;
        public CoolingMode Mode { get; private set; }
        public bool NeedsRecovery { get; private set; }
        public FanSample Latest { get; private set; }
        public string Status { get; private set; }
        public int RemainingSeconds { get { return Mode == CoolingMode.Quiet ? Math.Max(0, 300 - (int)(seconds() - began)) : 0; } }
        public QuietSession(IQuietDevice device, IRecoveryGuard recovery, Func<double> monotonicSeconds)
        {
            this.device = device; this.recovery = recovery; seconds = monotonicSeconds;
            Mode = CoolingMode.Automatic; Status = "尚未啟用低速；風扇由原有控制方式管理。";
        }

        public void Enable()
        {
            if (NeedsRecovery) throw new InvalidOperationException("請先恢復自動散熱。");
            Latest = device.Read();
            if (Latest == null || !Latest.Valid)
                throw new InvalidOperationException("溫度或負載資料尚未就緒，已停用低速控制。");
            if (Latest.ZoneC >= 60 || Latest.GpuC >= 55 || Latest.CpuLoad > 25 || Latest.GpuLoad > 30)
                throw new InvalidOperationException("目前溫度或負載偏高，請稍候再啟用低速。");
            try
            {
                recovery.Begin();
                NeedsRecovery = true;
                recovery.Arm();
                device.ApplyQuiet();
                began = lastWrite = seconds();
                busySince = -1;
                Mode = CoolingMode.Quiet;
                Status = "低速已送出，風扇將逐步降至約 1800 RPM。";
            }
            catch (Exception error)
            {
                if (NeedsRecovery) Restore("啟用失敗，已恢復自動散熱。");
                throw new InvalidOperationException("無法啟用低速：" + error.Message, error);
            }
        }

        public void Restore(string reason)
        {
            if (!NeedsRecovery) { Mode = CoolingMode.Automatic; Status = reason; return; }
            try
            {
                device.RestoreAutomatic();
                NeedsRecovery = false;
                Mode = CoolingMode.Automatic;
                recovery.Complete();
                Status = reason;
            }
            catch (Exception error)
            {
                Mode = CoolingMode.RecoveryFailed;
                Status = "尚未確認恢復，備援程序仍啟用中。請按「恢復自動」重試。";
                throw new InvalidOperationException(Status + " " + error.Message, error);
            }
        }

        public void Poll()
        {
            bool externalRestore = false;
            try { if (NeedsRecovery) externalRestore = recovery.WasRecovered; }
            catch (Exception) { Restore("備援程序中斷，已恢復自動散熱。"); return; }
            if (externalRestore)
            {
                NeedsRecovery = false;
                Mode = CoolingMode.Automatic;
                recovery.Complete();
                Status = "備援程序已恢復自動散熱；低速已結束。";
            }
            try { Latest = device.Read(); }
            catch (Exception error)
            {
                Latest = null;
                if (NeedsRecovery) Restore("監測中斷，已恢復自動散熱。");
                else Status = "讀取資料失敗：" + error.Message;
                return;
            }
            if (NeedsRecovery && (Latest == null || !Latest.Valid))
            { Restore("監測資料無效，已恢復自動散熱。"); return; }
            if (Mode == CoolingMode.Quiet && (Latest.ZoneC >= 60 || Latest.GpuC >= 55))
            { Restore("溫度升高，已恢復自動散熱。"); return; }
            if (Mode == CoolingMode.Quiet && seconds() - began >= 300)
            { Restore("本輪五分鐘已結束，已恢復自動散熱。"); return; }
            if (Mode != CoolingMode.Quiet) return;
            if (Latest.CpuLoad >= 70 || Latest.GpuLoad >= 50)
            { Restore("運算負載升高，已恢復自動散熱。"); return; }
            if (Latest.CpuLoad >= 35)
            {
                if (busySince < 0) busySince = seconds();
                if (seconds() - busySince >= 4)
                { Restore("CPU 持續忙碌，已恢復自動散熱。"); return; }
            }
            else busySince = -1;
            if (seconds() - began >= 15 && (Latest.Fan1 < 15 || Latest.Fan2 < 15 || Latest.Fan1 > 21 || Latest.Fan2 > 21))
            { Restore("韌體未維持低速範圍，已恢復自動散熱。"); return; }
            try
            {
                if (seconds() - lastWrite >= 30) { device.ApplyQuiet(); lastWrite = seconds(); }
                recovery.Pulse();
            }
            catch (Exception) { Restore("低速控制中斷，已恢復自動散熱。"); }
        }
    }
}
