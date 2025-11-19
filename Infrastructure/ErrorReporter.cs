using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Windows.Forms;
using Microsoft.VisualBasic.Devices;
using ToNRoundCounter.Application;
using WinFormsApp = System.Windows.Forms.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Reports unhandled errors to the logger and user.
    /// </summary>
    public class ErrorReporter : IErrorReporter
    {
        private readonly IEventLogger _logger;
        private readonly IEventBus _bus;

        public ErrorReporter(IEventLogger logger, IEventBus bus)
        {
            _logger = logger;
            _bus = bus;
        }

        public void Register()
        {
            _logger.LogEvent("ErrorReporter", "Registering global exception handlers.");
            WinFormsApp.ThreadException += (s, e) => Handle(e.Exception, false);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception exception)
                {
                    Handle(exception, e.IsTerminating);
                }
            };
            _logger.LogEvent("ErrorReporter", "Global exception handlers registered.");
        }

        public void Handle(Exception ex, bool isTerminating = false)
        {
            if (ex == null) return;
            _logger.LogEvent("ErrorReporter", $"Handling exception (terminating: {isTerminating}).");
            _logger.LogEvent("Unhandled", ex.ToString(), Serilog.Events.LogEventLevel.Error);
            _bus.Publish(new UnhandledExceptionOccurred(ex, isTerminating));
            try
            {
                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash-reports");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"crash-{DateTime.UtcNow:yyyyMMddHHmmssfff}.txt");
                var sb = new StringBuilder();
                sb.AppendLine($"Time: {DateTime.UtcNow:O}");
                sb.AppendLine($"OS: {Environment.OSVersion}");
                sb.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
                sb.AppendLine($"Processor Count: {Environment.ProcessorCount}");
                sb.AppendLine($"Machine Name: {Environment.MachineName}");
                sb.AppendLine($"User: {Environment.UserName}");
                try
                {
                    var info = new ComputerInfo();
                    sb.AppendLine($"Total Physical Memory: {FormatBytes(info.TotalPhysicalMemory)}");
                    sb.AppendLine($"Available Physical Memory: {FormatBytes(info.AvailablePhysicalMemory)}");
                }
                catch
                {
                    // ignore memory query errors
                }
                try
                {
                    var cpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                    var cpu = cpuSearcher.Get().Cast<ManagementObject>().FirstOrDefault()?[
                        "Name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(cpu))
                    {
                        sb.AppendLine($"CPU Name: {cpu}");
                    }
                }
                catch
                {
                    // ignore CPU query errors
                }
                try
                {
                    var gpuSearcher = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController");
                    foreach (ManagementObject obj in gpuSearcher.Get())
                    {
                        var name = obj["Name"]?.ToString();
                        var driver = obj["DriverVersion"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            sb.AppendLine($"GPU Name: {name}");
                        }
                        if (!string.IsNullOrWhiteSpace(driver))
                        {
                            sb.AppendLine($"GPU Driver Version: {driver}");
                        }
                        break;
                    }
                }
                catch
                {
                    // ignore GPU query errors
                }
                try
                {
                    var drive = new DriveInfo(Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory));
                    long total = drive.TotalSize;
                    long used = total - drive.AvailableFreeSpace;
                    double percent = total > 0 ? (double)used / total * 100 : 0;
                    try
                    {
                        var query =
                            $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{drive.Name.TrimEnd('\\')}'}} WHERE AssocClass = Win32_LogicalDiskToPartition";
                        using var partitionSearcher = new ManagementObjectSearcher(query);
                        foreach (ManagementObject partition in partitionSearcher.Get())
                        {
                            var driveQuery =
                                $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition";
                            using var driveSearcher = new ManagementObjectSearcher(driveQuery);
                            foreach (ManagementObject disk in driveSearcher.Get())
                            {
                                var model = disk["Model"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(model))
                                {
                                    sb.AppendLine($"SSD Name: {model}");
                                }
                                break;
                            }
                            break;
                        }
                    }
                    catch
                    {
                        // ignore SSD model query errors
                    }
                    sb.AppendLine($"SSD Total Size: {FormatBytes((ulong)total)}");
                    sb.AppendLine($"SSD Used Size: {FormatBytes((ulong)used)} ({percent:F2}%)");
                }
                catch
                {
                    // ignore SSD query errors
                }
                sb.AppendLine("Running Processes:");
                try
                {
                    var procs = Process.GetProcesses().OrderBy(p => p.ProcessName).Select(p => $"{p.ProcessName} ({p.Id})");
                    foreach (var proc in procs)
                    {
                        sb.AppendLine($"  {proc}");
                    }
                }
                catch
                {
                    sb.AppendLine("  <Failed to enumerate processes>");
                }
                sb.AppendLine();
                sb.AppendLine($"Exception Type: {ex.GetType()}");
                sb.AppendLine($"Message: {ex.Message}");
                if (!string.IsNullOrWhiteSpace(ex.ToString()))
                {
                    sb.AppendLine("Stack Trace:");
                    foreach (var line in ex.StackTrace
                        .Replace("\r\n", "\n")
                        .Split('\n', (char)StringSplitOptions.RemoveEmptyEntries))
                    {
                        sb.AppendLine(line);
                    }
                }
                else
                {
                    sb.AppendLine("Stack Trace: <none>");
                }
                File.WriteAllText(path, sb.ToString());
            }
            catch
            {
                // ignore file I/O errors
            }
            try
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // ignore UI errors
            }
            _logger.LogEvent("ErrorReporter", "Exception handling completed.");
        }

        private static string FormatBytes(ulong bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
            for (int i = units.Length - 1; i > 0; i--)
            {
                double value = bytes / Math.Pow(Constants.Data.BytesPerKilobyte, i);
                if (value >= 0.9)
                {
                    return $"{value:0.##}{units[i]}";
                }
            }
            return $"{bytes}B";
        }
    }
}
