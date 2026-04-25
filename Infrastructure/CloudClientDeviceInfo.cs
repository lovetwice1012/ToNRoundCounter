using System;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace ToNRoundCounter.Infrastructure
{
    public sealed class CloudClientDeviceInfo
    {
        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;

        [JsonPropertyName("device_name")]
        public string DeviceName { get; set; } = string.Empty;

        [JsonPropertyName("machine_name")]
        public string MachineName { get; set; } = string.Empty;

        [JsonPropertyName("os_description")]
        public string OsDescription { get; set; } = string.Empty;

        [JsonPropertyName("os_architecture")]
        public string OsArchitecture { get; set; } = string.Empty;

        [JsonPropertyName("processor_name")]
        public string ProcessorName { get; set; } = string.Empty;

        [JsonPropertyName("gpu_name")]
        public string GpuName { get; set; } = string.Empty;

        [JsonPropertyName("memory_mb")]
        public long MemoryMb { get; set; }

        public static CloudClientDeviceInfo Create()
        {
            var machineName = SafeString(Environment.MachineName);
            var processorName = QueryFirstWmiString("SELECT Name FROM Win32_Processor", "Name");
            var gpuName = QueryJoinedWmiStrings("SELECT Name FROM Win32_VideoController", "Name");
            var memoryMb = QueryTotalPhysicalMemoryMb();
            var machineGuid = ReadMachineGuid();

            return new CloudClientDeviceInfo
            {
                DeviceName = machineName,
                MachineName = machineName,
                OsDescription = SafeString(RuntimeInformation.OSDescription),
                OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                ProcessorName = processorName,
                GpuName = gpuName,
                MemoryMb = memoryMb,
                DeviceId = CreateDeviceId(machineGuid, machineName, processorName),
            };
        }

        private static string CreateDeviceId(string machineGuid, string machineName, string processorName)
        {
            var source = string.Join("|", machineGuid, machineName, processorName);
            if (string.IsNullOrWhiteSpace(source.Replace("|", string.Empty)))
            {
                source = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            }

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(source));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string ReadMachineGuid()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", false);
                return SafeString(key?.GetValue("MachineGuid") as string);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string QueryFirstWmiString(string query, string propertyName)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject obj in searcher.Get())
                {
                    using (obj)
                    {
                        var value = obj[propertyName]?.ToString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return SafeString(value);
                        }
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string QueryJoinedWmiStrings(string query, string propertyName)
        {
            var builder = new StringBuilder();
            try
            {
                using var searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject obj in searcher.Get())
                {
                    using (obj)
                    {
                        var value = SafeString(obj[propertyName]?.ToString());
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            continue;
                        }

                        if (builder.Length > 0)
                        {
                            builder.Append(", ");
                        }
                        builder.Append(value);
                    }
                }
            }
            catch
            {
            }

            return builder.ToString();
        }

        private static long QueryTotalPhysicalMemoryMb()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                    using (obj)
                    {
                        if (long.TryParse(obj["TotalPhysicalMemory"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
                        {
                            return Math.Max(0, bytes / (1024 * 1024));
                        }
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private static string SafeString(string? value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
