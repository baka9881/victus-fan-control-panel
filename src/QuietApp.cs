using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VictusQuiet
{
    static class Program
    {
        public static bool IsAdmin()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        static int Verify(string path)
        {
            using (StreamWriter log = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                log.AutoFlush = true;
                log.WriteLine("VictusQuiet 完成版短暫驗證 " + DateTimeOffset.Now.ToString("o"));
                if (!IsAdmin()) { log.WriteLine("ERROR: administrator privileges required"); return 2; }
                using (HpQuietDevice device = new HpQuietDevice())
                using (ProcessRecoveryGuard guard = new ProcessRecoveryGuard())
                {
                    QuietSession session = new QuietSession(device, guard, () => HpQuietDevice.Seconds);
                    try
                    {
                        bool ready = false;
                        for (int i = 0; i < 45; i++)
                        {
                            Thread.Sleep(2000); session.Poll(); FanSample sample = session.Latest;
                            if (sample != null) log.WriteLine("Preflight zone=" + sample.ZoneC.ToString("0.0") + " gpu=" + sample.GpuC + " cpuLoad=" + sample.CpuLoad.ToString("0.0"));
                            if (sample != null && sample.Valid && sample.ZoneC < 60 && sample.GpuC < 55 && sample.CpuLoad <= 25 && sample.GpuLoad <= 30) { ready = true; break; }
                        }
                        if (!ready) { log.WriteLine("SKIPPED: no cool idle window; no quiet write"); return 3; }
                        bool reachedTarget = false;
                        session.Enable(); log.WriteLine("QUIET ENABLED; independent recovery process ready");
                        for (int i = 0; i < 6; i++)
                        {
                            Thread.Sleep(2000); session.Poll(); FanSample sample = session.Latest;
                            log.WriteLine("Mode=" + session.Mode + " fans=" + (sample == null ? "unknown" : sample.Fan1 + "/" + sample.Fan2) + " zone=" + (sample == null ? "unknown" : sample.ZoneC.ToString("0.0")) + " gpu=" + (sample == null ? "unknown" : sample.GpuC.ToString()) + " status=" + session.Status);
                            if (session.Mode == CoolingMode.Quiet && sample != null && sample.Fan1 >= 16 && sample.Fan1 <= 20 && sample.Fan2 >= 16 && sample.Fan2 <= 20) reachedTarget = true;
                            if (session.Mode != CoolingMode.Quiet) break;
                        }
                        session.Restore("完成驗證，已恢復自動散熱。");
                        log.WriteLine("RESTORED: NeedsRecovery=" + session.NeedsRecovery);
                        if (!reachedTarget) { log.WriteLine("INCOMPLETE: target range not observed; restoration completed"); return 3; }
                        log.WriteLine("PASS: low-speed sequence completed; restoration confirmed"); return 0;
                    }
                    catch (Exception error) { log.WriteLine("ERROR: " + error); return 1; }
                    finally
                    {
                        if (session.NeedsRecovery)
                        {
                            try { session.Restore("驗證結束，已恢復自動散熱。"); log.WriteLine("Finally restored"); }
                            catch (Exception error) { log.WriteLine("RESTORATION ERROR; independent recovery remains armed: " + error.Message); }
                        }
                    }
                }
            }
        }
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--rescue") { int parent; return Int32.TryParse(args[2], out parent) ? ProcessRecoveryGuard.Watch(args[1], parent) : 2; }
            if (args.Length == 2 && args[0] == "--guard-check")
            {
                try { using (ProcessRecoveryGuard guard = new ProcessRecoveryGuard()) { guard.Begin(); guard.Complete(); } File.WriteAllText(args[1], "PASS: independent process opened named shared memory and acknowledged readiness; no fan writes"); return 0; }
                catch (Exception error) { File.WriteAllText(args[1], error.ToString()); return 1; }
            }
            if (args.Length == 2 && args[0] == "--verify")
            {
                bool verifyOwns = false;
                using (Mutex control = new Mutex(false, @"Local\VictusQuiet-88F8-Control"))
                {
                    try { try { verifyOwns = control.WaitOne(0); } catch (AbandonedMutexException) { verifyOwns = true; }
                        if (!verifyOwns) { File.WriteAllText(args[1], "Another control instance is running; verification skipped"); return 2; }
                        return Verify(args[1]);
                    }
                    finally { if (verifyOwns) control.ReleaseMutex(); }
                }
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool render = args.Length == 2 && (args[0] == "--render" || args[0] == "--render-auto" || args[0] == "--render-locked" || args[0] == "--render-controls" || args[0] == "--render-protection");
            string scene = render && args[0] != "--render" ? args[0].Substring(9) : "quiet";
            using (QuietWindow window = new QuietWindow(render, scene))
            {
                if (render)
                {
                    window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-16000,-16000);
                    window.Show(); Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(args[1]); }
                    return 0;
                }
                bool owns = false;
                using (Mutex control = new Mutex(false, @"Local\VictusQuiet-88F8-Control"))
                {
                    try
                    {
                        if (IsAdmin())
                        {
                            try { owns = control.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
                            if (!owns) { MessageBox.Show("已有 Victus 安靜風扇正在執行，請使用原來的視窗。", "Victus 安靜風扇"); return 2; }
                        }
                        Application.Run(window);
                    }
                    finally { if (owns) control.ReleaseMutex(); }
                }
            }
            return 0;
        }
    }
}
