using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace VictusQuiet
{
    public sealed class HpQuietDevice : IQuietDevice
    {
        ManagementObject mailbox;
        bool gpuReady;
        IntPtr gpu;
        ulong lastIdle, lastKernel, lastUser;
        long cpuAt;
        double cpuValue = Double.NaN;
        public static double Seconds { get { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; } }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
        [DllImport("nvml.dll")] static extern int nvmlInit_v2();
        [DllImport("nvml.dll")] static extern int nvmlShutdown();
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temperature);
        [StructLayout(LayoutKind.Sequential)] struct Utilization { public uint Gpu, Memory; }
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization utilization);

        static string ReadIdentity(string className, string property)
        {
            using (ManagementObjectSearcher query = new ManagementObjectSearcher(@"root\CIMV2", "SELECT " + property + " FROM " + className))
            using (ManagementObjectCollection rows = query.Get())
                foreach (ManagementObject row in rows) using (row) return Convert.ToString(row[property]);
            return "";
        }
        public static string CheckMachine()
        {
            string maker = ReadIdentity("Win32_ComputerSystem", "Manufacturer");
            string board = ReadIdentity("Win32_BaseBoard", "Product");
            string bios = ReadIdentity("Win32_BIOS", "SMBIOSBIOSVersion");
            if (maker != "HP" || board != "88F8" || bios != "F.25")
                throw new NotSupportedException("此版本只開放已測試的 HP 88F8／BIOS F.25。");
            return "HP Victus 16-d0xxx  ·  88F8 / F.25";
        }
        public HpQuietDevice() { CheckMachine(); ReadCpu(); }

        ManagementObject Mailbox()
        {
            if (mailbox != null) return mailbox;
            using (ManagementObjectSearcher search = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM hpqBIntM"))
            using (ManagementObjectCollection rows = search.Get())
                foreach (ManagementObject row in rows)
                { if (mailbox == null) mailbox = row; else row.Dispose(); }
            if (mailbox == null) throw new NotSupportedException("找不到 HP 風扇控制介面。");
            return mailbox;
        }
        byte[] Call(uint operation, byte[] payload, int outputSize)
        {
            // This utility exposes no arbitrary command API. Its writes are fixed below.
            using (ManagementClass schema = new ManagementClass(@"root\WMI", "hpqBDataIn", null))
            using (ManagementObject data = schema.CreateInstance())
            using (ManagementBaseObject arguments = Mailbox().GetMethodParameters("hpqBIOSInt" + outputSize))
            {
                data["Sign"] = new byte[] { 0x53, 0x45, 0x43, 0x55 };
                data["Command"] = (uint)0x20008;
                data["CommandType"] = operation;
                data["Size"] = (uint)payload.Length;
                data["hpqBData"] = payload;
                arguments["InData"] = data;
                InvokeMethodOptions options = new InvokeMethodOptions(); options.Timeout = TimeSpan.FromSeconds(3);
                using (ManagementBaseObject reply = Mailbox().InvokeMethod("hpqBIOSInt" + outputSize, arguments, options))
                {
                    if (reply == null || reply["OutData"] == null) throw new InvalidOperationException("HP 介面沒有回傳結果。");
                    using (ManagementBaseObject result = (ManagementBaseObject)reply["OutData"])
                    {
                        if (result["rwReturnCode"] == null) throw new InvalidOperationException("HP 介面缺少狀態碼。");
                        uint status = Convert.ToUInt32(result["rwReturnCode"]);
                        if (status != 0) throw new InvalidOperationException("BIOS 拒絕操作，狀態碼 " + status + "。");
                        if (outputSize == 0) return new byte[0];
                        byte[] bytes = result["Data"] as byte[];
                        if (bytes == null || bytes.Length < outputSize) throw new InvalidOperationException("風扇回報資料不完整。");
                        return bytes;
                    }
                }
            }
        }
        int[] ReadFans()
        {
            byte[] bytes = Call(0x2D, new byte[4], 128);
            if (bytes[0] > 80 || bytes[1] > 80) throw new InvalidOperationException("風扇速度等級異常。");
            return new int[] { bytes[0], bytes[1] };
        }
        void WritePositiveLevel(byte level)
        {
            if (level != 18 && level != 33 && level != 50) throw new InvalidOperationException("未開放此轉速等級。");
            byte[] values = new byte[128]; values[0] = values[1] = level;
            Call(0x10, new byte[4], 4); // Deliberate manual-state keepalive, never used by read-only polling.
            Call(0x2E, values, 0);
        }
        public void ApplyQuiet()
        {
            Call(0x27, new byte[] { 0 }, 0);
            Call(0x1A, new byte[] { 255, 0, 0, 0 }, 0);
            WritePositiveLevel(18);
        }
        public void RestoreAutomatic()
        {
            Call(0x27, new byte[] { 0 }, 0);
            WritePositiveLevel(33);
            Call(0x1A, new byte[] { 255, 0, 1, 0 }, 0);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Thread.Sleep(400);
                int[] fans = ReadFans();
                if (fans[0] >= 18 && fans[1] >= 18) return;
            }
            throw new InvalidOperationException("尚未確認兩個風扇恢復正轉。");
        }
        public void EmergencyHighCooling()
        {
            Call(0x1A, new byte[] { 255, 0, 0, 0 }, 0);
            WritePositiveLevel(50);
            Call(0x27, new byte[] { 1 }, 0);
        }
        double ReadCpu()
        {
            ulong idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) return Double.NaN;
            long now = Stopwatch.GetTimestamp();
            if (cpuAt != 0 && (double)(now - cpuAt) / Stopwatch.Frequency < 0.5) return cpuValue;
            if (cpuAt != 0)
            {
                ulong total = (kernel - lastKernel) + (user - lastUser);
                ulong idleDelta = idle - lastIdle;
                cpuValue = total == 0 || idleDelta > total ? Double.NaN : 100.0 * (total - idleDelta) / total;
            }
            lastIdle = idle; lastKernel = kernel; lastUser = user; cpuAt = now;
            return cpuValue;
        }
        public FanSample Read()
        {
            double cpu = ReadCpu(), zone = Double.NaN;
            EnumerationOptions options = new EnumerationOptions(); options.Timeout = TimeSpan.FromSeconds(3);
            using (ManagementObjectSearcher search = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", options))
            using (ManagementObjectCollection rows = search.Get())
                foreach (ManagementObject row in rows) using (row)
                {
                    double value = Convert.ToDouble(row["CurrentTemperature"]) / 10.0 - 273.15;
                    if (value < 10 || value > 115) throw new InvalidOperationException("ACPI 溫度無效。");
                    zone = Double.IsNaN(zone) ? value : Math.Max(zone, value);
                }
            if (Double.IsNaN(zone)) throw new InvalidOperationException("讀不到 ACPI 熱區溫度。");
            if (!gpuReady)
            {
                if (nvmlInit_v2() != 0) throw new InvalidOperationException("無法啟動 NVIDIA 溫度讀取。");
                gpuReady = true;
                if (nvmlDeviceGetHandleByIndex_v2(0, out gpu) != 0) throw new InvalidOperationException("找不到 NVIDIA GPU。");
            }
            uint temperature; Utilization load;
            if (nvmlDeviceGetTemperature(gpu, 0, out temperature) != 0 || nvmlDeviceGetUtilizationRates(gpu, out load) != 0)
                throw new InvalidOperationException("GPU 溫度或負載讀取失敗。");
            int[] fans = ReadFans();
            return new FanSample(zone, temperature, cpu, load.Gpu, fans[0], fans[1]);
        }
        public void Dispose()
        {
            if (mailbox != null) mailbox.Dispose();
            if (gpuReady) { try { nvmlShutdown(); } catch (Exception) { } }
        }
    }
}
