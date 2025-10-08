#nullable enable

namespace ToNRoundCounter.Application.Recording
{
    internal readonly struct HardwareEncoderSelection
    {
        public HardwareEncoderSelection(HardwareAccelerationApi api, int adapterHighPart, uint adapterLowPart, bool hasAdapter, bool allowSoftwareFallback)
        {
            Api = api;
            AdapterHighPart = adapterHighPart;
            AdapterLowPart = adapterLowPart;
            HasAdapter = hasAdapter;
            AllowSoftwareFallback = allowSoftwareFallback;
        }

        public HardwareAccelerationApi Api { get; }

        public int AdapterHighPart { get; }

        public uint AdapterLowPart { get; }

        public bool HasAdapter { get; }

        public bool AllowSoftwareFallback { get; }
    }
}
