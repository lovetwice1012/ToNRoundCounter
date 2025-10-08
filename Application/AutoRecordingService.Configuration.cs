#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ToNRoundCounter.Application.Recording;

namespace ToNRoundCounter.Application
{
    public sealed partial class AutoRecordingService
    {
        private static string NormalizeExtension(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return SupportedExtensions[0];
            }

            var trimmed = extension.Trim().TrimStart('.');
            foreach (var candidate in SupportedExtensions)
            {
                if (trimmed.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return SupportedExtensions[0];
        }

        internal static readonly string[] SupportedExtensions = new[]
        {
            "mp4",
            "avi",
            "mov",
            "wmv",
            "mpg",
            "mkv",
            "flv",
            "asf",
            "vob",
            "gif",
        };

        internal const string DefaultCodec = "h264";
        internal const string DefaultHardwareEncoderOptionId = "auto";
        internal const string SoftwareHardwareEncoderOptionId = "software";
        private const string D3D11HardwareOptionPrefix = "d3d11:";

        public readonly struct RecordingCodecInfo
        {
            public RecordingCodecInfo(string codecId, string localizationKey, bool supportsAudio)
            {
                CodecId = codecId;
                LocalizationKey = localizationKey;
                SupportsAudio = supportsAudio;
            }

            public string CodecId { get; }

            public string LocalizationKey { get; }

            public bool SupportsAudio { get; }
        }

        public readonly struct HardwareEncoderOption
        {
            public HardwareEncoderOption(string id, string localizationKey, string? adapterName)
            {
                Id = id;
                LocalizationKey = localizationKey;
                AdapterName = adapterName;
            }

            public string Id { get; }

            public string LocalizationKey { get; }

            public string? AdapterName { get; }
        }

        public static IReadOnlyList<RecordingCodecInfo> GetCodecOptions(string extension)
        {
            string normalized = NormalizeExtension(extension);

            if (string.Equals(normalized, "gif", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { new RecordingCodecInfo("gif", "AutoRecording_CodecOption_GIF", false) };
            }

            if (string.Equals(normalized, "avi", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { new RecordingCodecInfo("raw", "AutoRecording_CodecOption_Raw", false) };
            }

            var codecs = MediaFoundationFrameWriter.GetCodecInfo(normalized);
            if (codecs.Count == 0)
            {
                return new[] { new RecordingCodecInfo(DefaultCodec, "AutoRecording_CodecOption_Fallback", true) };
            }

            return codecs;
        }

        public static string NormalizeCodec(string extension, string? codecId)
        {
            var options = GetCodecOptions(extension);
            if (options.Count == 0)
            {
                return DefaultCodec;
            }

            string candidate = (codecId ?? string.Empty).Trim();
            foreach (var option in options)
            {
                if (string.Equals(option.CodecId, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return option.CodecId;
                }
            }

            return options[0].CodecId;
        }

        public static int NormalizeVideoBitrate(int bitrate)
        {
            if (bitrate <= 0)
            {
                return 0;
            }

            return bitrate > 500_000_000 ? 500_000_000 : bitrate;
        }

        public static int NormalizeAudioBitrate(int bitrate)
        {
            if (bitrate <= 0)
            {
                return 0;
            }

            return bitrate > 1_000_000 ? 1_000_000 : bitrate;
        }

        public static IReadOnlyList<HardwareEncoderOption> GetHardwareEncoderOptions()
        {
            var options = new List<HardwareEncoderOption>
            {
                new HardwareEncoderOption(DefaultHardwareEncoderOptionId, "AutoRecording_HardwareOption_Auto", null),
                new HardwareEncoderOption(SoftwareHardwareEncoderOptionId, "AutoRecording_HardwareOption_Software", null),
            };

            try
            {
                foreach (var adapter in MediaFoundationFrameWriter.GetHardwareAdapterDescriptors())
                {
                    string id = BuildD3D11OptionId(adapter.LuidHighPart, adapter.LuidLowPart);
                    options.Add(new HardwareEncoderOption(id, "AutoRecording_HardwareOption_D3D11", adapter.Description));
                }
            }
            catch
            {
            }

            return options;
        }

        public static string NormalizeHardwareOption(string? optionId)
        {
            string candidate = (optionId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(candidate))
            {
                return DefaultHardwareEncoderOptionId;
            }

            foreach (var option in GetHardwareEncoderOptions())
            {
                if (string.Equals(option.Id, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return option.Id;
                }
            }

            if (candidate.StartsWith(D3D11HardwareOptionPrefix, StringComparison.OrdinalIgnoreCase) && TryParseAdapterId(candidate, out _, out _))
            {
                return candidate;
            }

            return DefaultHardwareEncoderOptionId;
        }

        private static bool CodecSupportsAudio(string extension, string codecId)
        {
            foreach (var option in GetCodecOptions(extension))
            {
                if (string.Equals(option.CodecId, codecId, StringComparison.OrdinalIgnoreCase))
                {
                    return option.SupportsAudio;
                }
            }

            return true;
        }

        private static bool TryParseAdapterId(string optionId, out int highPart, out uint lowPart)
        {
            highPart = 0;
            lowPart = 0;

            if (!optionId.StartsWith(D3D11HardwareOptionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string payload = optionId.Substring(D3D11HardwareOptionPrefix.Length);
            string[] segments = payload.Split(':');
            if (segments.Length == 2)
            {
                if (int.TryParse(segments[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out highPart) &&
                    uint.TryParse(segments[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out lowPart))
                {
                    return true;
                }
            }
            else if (payload.Length == 16)
            {
                if (int.TryParse(payload.Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out highPart) &&
                    uint.TryParse(payload.Substring(8, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out lowPart))
                {
                    return true;
                }
            }

            highPart = 0;
            lowPart = 0;
            return false;
        }

        private static string BuildD3D11OptionId(int highPart, uint lowPart)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:X8}:{2:X8}", D3D11HardwareOptionPrefix, highPart, lowPart);
        }

        private static HardwareEncoderSelection ParseHardwareSelection(string optionId)
        {
            if (string.Equals(optionId, SoftwareHardwareEncoderOptionId, StringComparison.OrdinalIgnoreCase))
            {
                return new HardwareEncoderSelection(HardwareAccelerationApi.Software, 0, 0, false, false);
            }

            if (string.Equals(optionId, DefaultHardwareEncoderOptionId, StringComparison.OrdinalIgnoreCase))
            {
                return new HardwareEncoderSelection(HardwareAccelerationApi.Auto, 0, 0, false, true);
            }

            if (TryParseAdapterId(optionId, out int high, out uint low))
            {
                return new HardwareEncoderSelection(HardwareAccelerationApi.Direct3D11, high, low, true, true);
            }

            return new HardwareEncoderSelection(HardwareAccelerationApi.Auto, 0, 0, false, true);
        }

        private static string GetHardwareOptionDisplay(string optionId)
        {
            foreach (var option in GetHardwareEncoderOptions())
            {
                if (string.Equals(option.Id, optionId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(option.AdapterName))
                    {
                        return string.Format(CultureInfo.InvariantCulture, "Direct3D 11 ({0})", option.AdapterName);
                    }

                    if (string.Equals(option.Id, SoftwareHardwareEncoderOptionId, StringComparison.OrdinalIgnoreCase))
                    {
                        return "Software";
                    }

                    if (string.Equals(option.Id, DefaultHardwareEncoderOptionId, StringComparison.OrdinalIgnoreCase))
                    {
                        return "Auto";
                    }
                }
            }

            if (TryParseAdapterId(optionId, out _, out _))
            {
                return string.Format(CultureInfo.InvariantCulture, "Direct3D 11 ({0})", optionId.Substring(D3D11HardwareOptionPrefix.Length));
            }

            return optionId;
        }

        private static int NormalizeFrameRate(int frameRate)
        {
            if (frameRate < 5)
            {
                return 5;
            }

            if (frameRate > 240)
            {
                return 240;
            }

            return frameRate;
        }

        private string ResolveOutputDirectory()
        {
            string configured = (_settings.AutoRecordingOutputDirectory ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(configured))
            {
                configured = "recordings";
            }

            if (!Path.IsPathRooted(configured))
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
                return Path.GetFullPath(Path.Combine(baseDirectory, configured));
            }

            return configured;
        }

        private static string GenerateOutputFileName(string triggerDetails, string extension)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string sanitizedTrigger = SanitizeForFileName(triggerDetails ?? string.Empty);
            if (string.IsNullOrWhiteSpace(sanitizedTrigger))
            {
                sanitizedTrigger = "recording";
            }

            string extWithDot = extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}";
            return $"{timestamp}_{sanitizedTrigger}{extWithDot}";
        }

        private static string SanitizeForFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (invalidChars.Contains(c) || char.IsWhiteSpace(c))
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(c);
                }
            }

            string sanitized = builder.ToString();
            while (sanitized.Contains("__"))
            {
                sanitized = sanitized.Replace("__", "_");
            }

            return sanitized.Trim('_');
        }
    }
}
