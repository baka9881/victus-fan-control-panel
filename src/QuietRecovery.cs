using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace VictusQuiet
{
    // Layout: 0 needs recovery, 8 monotonic heartbeat, 16 ready, 20 externally recovered,
    // 24 armed, 28 completed, 32 monotonic start. Nothing is written to disk.
    public sealed class ProcessRecoveryGuard : IRecoveryGuard
    {
        MemoryMappedFile map;
        MemoryMappedViewAccessor view;
        Process child;
        public bool WasRecovered
        {
            get
            {
                if (view == null) return false;
                int outcome = view.ReadInt32(20);
                if (outcome == 1) return true;
                if (view.ReadInt32(0) == 1 && (outcome == 2 || child == null || child.HasExited))
                    throw new InvalidOperationException("備援程序已中斷，正在恢復自動散熱。");
                return false;
            }
        }
        public void Begin()
        {
            Dispose();
            string name = @"Local\VictusQuiet-" + Guid.NewGuid().ToString("N");
            map = MemoryMappedFile.CreateNew(name, 64);
            view = map.CreateViewAccessor();
            view.Write(8, Stopwatch.GetTimestamp());
            ProcessStartInfo start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName);
            start.Arguments = "--rescue " + name + " " + Process.GetCurrentProcess().Id;
            start.UseShellExecute = false; start.CreateNoWindow = true; start.WindowStyle = ProcessWindowStyle.Hidden;
            child = Process.Start(start);
            Stopwatch timer = Stopwatch.StartNew();
            while (view.ReadInt32(16) != 1)
            {
                if (timer.Elapsed.TotalSeconds > 4 || child.HasExited)
                { Dispose(); throw new InvalidOperationException("備援恢復程序未能啟動；已停用低速。"); }
                Thread.Sleep(50);
            }
        }
        public void Arm()
        {
            if (view == null || view.ReadInt32(16) != 1) throw new InvalidOperationException("備援尚未就緒。");
            view.Write(8, Stopwatch.GetTimestamp()); view.Write(32, Stopwatch.GetTimestamp());
            view.Write(0, 1); view.Write(24, 1); view.Flush();
        }
        public void Pulse() { if (view != null) { view.Write(8, Stopwatch.GetTimestamp()); view.Flush(); } }
        public void Complete() { if (view != null) { view.Write(0, 0); view.Write(28, 1); view.Flush(); } }
        public void Dispose()
        {
            if (view != null)
            {
                if (view.ReadInt32(0) == 0) { view.Write(28, 1); view.Flush(); }
                view.Dispose(); view = null;
            }
            if (map != null) { map.Dispose(); map = null; }
            if (child != null) { child.Dispose(); child = null; }
        }
        static bool ParentAlive(int pid)
        {
            try { using (Process parent = Process.GetProcessById(pid)) return !parent.HasExited; }
            catch (Exception) { return false; }
        }
        public static int Watch(string name, int parentPid)
        {
            if (!name.StartsWith(@"Local\VictusQuiet-", StringComparison.Ordinal) || name.Length != 50) return 2;
            try
            {
                using (MemoryMappedFile watched = MemoryMappedFile.OpenExisting(name))
                using (MemoryMappedViewAccessor shared = watched.CreateViewAccessor())
                {
                    shared.Write(16, 1); shared.Flush();
                    while (true)
                    {
                        bool required = shared.ReadInt32(0) == 1;
                        bool alive = ParentAlive(parentPid);
                        if (!required && (shared.ReadInt32(28) == 1 || !alive)) return 0;
                        if (required && shared.ReadInt32(24) == 1)
                        {
                            double age = (double)(Stopwatch.GetTimestamp() - shared.ReadInt64(8)) / Stopwatch.Frequency;
                            double duration = (double)(Stopwatch.GetTimestamp() - shared.ReadInt64(32)) / Stopwatch.Frequency;
                            if (!alive || age > 8 || duration > 307)
                            {
                                using (HpQuietDevice device = new HpQuietDevice())
                                {
                                    try { device.RestoreAutomatic(); }
                                    catch (Exception)
                                    {
                                        device.EmergencyHighCooling();
                                        // Keep ownership/recovery marked unresolved; a living UI can retry.
                                        shared.Write(20, 2); shared.Flush(); return 3;
                                    }
                                }
                                shared.Write(0, 0); shared.Write(20, 1); shared.Flush(); return 0;
                            }
                        }
                        Thread.Sleep(200);
                    }
                }
            }
            catch (Exception error)
            {
                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VictusQuiet-recovery-error.txt"), DateTimeOffset.Now + " " + error.Message + Environment.NewLine); }
                catch (Exception) { }
                return 2;
            }
        }
    }
}
