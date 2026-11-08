using System;
using VictusQuiet;

sealed class FirmwareSimulator : IQuietDevice
{
    public FanSample Sample = new FanSample(54, 47, 10, 8, 18, 18);
    public bool QuietApplied;
    public bool FailRead;
    public bool FailRestore;
    public FanSample Read() { if (FailRead) throw new Exception("Sensor unavailable"); return Sample; }
    public void ApplyQuiet() { QuietApplied = true; }
    public void RestoreAutomatic() { if (FailRestore) throw new Exception("Firmware refused restoration"); QuietApplied = false; }
    public void Dispose() { }
}
sealed class RecoverySimulator : IRecoveryGuard
{
    public bool WasRecovered { get; set; }
    public void Begin() { }
    public void Arm() { }
    public void Pulse() { }
    public void Complete() { }
    public void Dispose() { }
}
static class QuietTests
{
    static void MustReject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Expected a refusal");
    }
    static int Main()
    {
        try
        {
            FirmwareSimulator device = new FirmwareSimulator();
            device.Sample = new FanSample(Double.NaN, 47, 10, 8, 18, 18);
            QuietSession session = new QuietSession(device, new RecoverySimulator(), () => 0);
            MustReject(() => session.Enable());
            if (device.QuietApplied || session.NeedsRecovery) throw new Exception("Missing temperature must not enable quiet mode");
            Console.WriteLine("PASS: missing thermal data prevents low-speed control");
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => 0);
            session.Enable();
            device.FailRead = true;
            session.Poll();
            if (device.QuietApplied || session.NeedsRecovery || session.Mode != CoolingMode.Automatic)
                throw new Exception("Lost telemetry must restore automatic cooling");
            Console.WriteLine("PASS: missing telemetry restores automatic cooling");
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => 0);
            session.Enable();
            device.Sample = new FanSample(60, 47, 10, 8, 18, 18);
            session.Poll();
            if (device.QuietApplied || session.NeedsRecovery) throw new Exception("A hot thermal zone must restore automatic cooling");
            Console.WriteLine("PASS: rising temperature restores automatic cooling");
            double clock = 0;
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => clock);
            session.Enable();
            clock = 301;
            session.Poll();
            if (device.QuietApplied || session.NeedsRecovery) throw new Exception("The five-minute limit must restore automatic cooling");
            Console.WriteLine("PASS: five-minute limit restores automatic cooling");
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => 0);
            session.Enable();
            device.Sample = new FanSample(54, 47, 75, 8, 18, 18);
            session.Poll();
            if (device.QuietApplied || session.NeedsRecovery) throw new Exception("High CPU load must restore automatic cooling");
            Console.WriteLine("PASS: high CPU load restores automatic cooling");
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => 0);
            session.Enable();
            device.FailRestore = true;
            MustReject(() => session.Restore("Close"));
            if (!session.NeedsRecovery || session.Mode != CoolingMode.RecoveryFailed)
                throw new Exception("Failed restoration must remain visible and armed");
            device.FailRestore = false;
            session.Restore("Retry");
            if (session.NeedsRecovery || device.QuietApplied) throw new Exception("Restoration retry failed");
            Console.WriteLine("PASS: failed restoration stays armed and supports retry");
            device = new FirmwareSimulator();
            RecoverySimulator recovery = new RecoverySimulator();
            session = new QuietSession(device, recovery, () => 0);
            session.Enable();
            recovery.WasRecovered = true;
            device.QuietApplied = false;
            session.Poll();
            if (session.NeedsRecovery || session.Mode != CoolingMode.Automatic)
                throw new Exception("An independent recovery must end quiet mode");
            Console.WriteLine("PASS: independent recovery ends the low-speed session");
            clock = 0;
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => clock);
            session.Enable();
            device.Sample = new FanSample(54, 47, 40, 8, 18, 18);
            clock = 1; session.Poll();
            clock = 5; session.Poll();
            if (device.QuietApplied || session.NeedsRecovery) throw new Exception("Sustained CPU work must end low speed");
            Console.WriteLine("PASS: sustained CPU load ends low speed");
            clock = 0;
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => clock);
            session.Enable();
            device.Sample = new FanSample(54, 47, 10, 8, 0, 0);
            clock = 16; session.Poll();
            if (device.QuietApplied || session.NeedsRecovery) throw new Exception("Unresponsive fans must end low speed");
            Console.WriteLine("PASS: stalled or overridden fans end low speed");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
