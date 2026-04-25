using ToNRoundCounter.Application.Recording;
using Xunit;

namespace ToNRoundCounter.Tests
{
    public class AudioSignalProcessorTests
    {
        [Fact]
        public void ComputeTargetGain_UsesMeterRatio_WhenItAttenuates()
        {
            float gain = AudioSignalProcessor.ComputeTargetGain(capturedPeak: 1.0f, meterPeak: 0.5f, endpointGain: 0.8f);

            Assert.Equal(0.5f, gain, 3);
        }

        [Fact]
        public void ComputeTargetGain_NeverBoostsAboveEndpointOrUnity()
        {
            float gain = AudioSignalProcessor.ComputeTargetGain(capturedPeak: 0.4f, meterPeak: 0.9f, endpointGain: 0.6f);

            Assert.Equal(0.5f, gain, 3);
        }

        [Fact]
        public void ComputeTargetGain_DoesNotExceedEndpoint_WhenMeterRunsHot()
        {
            float gain = AudioSignalProcessor.ComputeTargetGain(capturedPeak: 0.5f, meterPeak: 0.95f, endpointGain: 0.35f);

            Assert.Equal(0.35f, gain, 3);
        }

        [Fact]
        public void ComputeTargetGain_FallsBackToEndpointGain_WhenMeterUnavailable()
        {
            float gain = AudioSignalProcessor.ComputeTargetGain(capturedPeak: 1.0f, meterPeak: null, endpointGain: 0.42f);

            Assert.Equal(0.42f, gain, 3);
        }

        [Fact]
        public void ComputeTargetGain_AppliesPeakSafetyCap_WhenInputAlreadyHot()
        {
            float gain = AudioSignalProcessor.ComputeTargetGain(capturedPeak: 2.0f, meterPeak: null, endpointGain: 1.0f);

            Assert.Equal(0.4f, gain, 3);
        }

        [Fact]
        public void ComputeTargetGain_UsesLowerOfMeterAndSafetyCap()
        {
            float gain = AudioSignalProcessor.ComputeTargetGain(capturedPeak: 1.5f, meterPeak: 1.0f, endpointGain: 1.0f);

            Assert.Equal(0.5f, gain, 3);
        }

        [Fact]
        public void ComputeTargetGain_UsesGlobalOutputCeiling_WhenEndpointIsUnity()
        {
            float gain = AudioSignalProcessor.ComputeTargetGain(capturedPeak: 1.0f, meterPeak: 1.0f, endpointGain: 1.0f);

            Assert.Equal(0.5f, gain, 3);
        }

        [Fact]
        public void ResolvePacketGain_AttenuatesGradually_WhenTargetDropsWithoutSafetyPressure()
        {
            float gain = AudioSignalProcessor.ResolvePacketGain(capturedPeak: 1.0f, meterPeak: 0.35f, endpointGain: 1.0f, previousGain: 0.9f);

            Assert.Equal(0.707f, gain, 3);
        }

        [Fact]
        public void ResolvePacketGain_ReleasesSlowly_WhenTargetRises()
        {
            float gain = AudioSignalProcessor.ResolvePacketGain(capturedPeak: 1.0f, meterPeak: 0.8f, endpointGain: 1.0f, previousGain: 0.3f);

            Assert.Equal(0.32f, gain, 2);
        }

        [Fact]
        public void ResolvePacketGain_UsesHardSafetyCap_WhenPacketWouldOtherwiseClip()
        {
            float gain = AudioSignalProcessor.ResolvePacketGain(capturedPeak: 2.0f, meterPeak: 1.0f, endpointGain: 1.0f, previousGain: 0.9f);

            Assert.Equal(0.4f, gain, 3);
        }

        [Fact]
        public void ResolvePacketGain_IgnoresTinyMeterFluctuations_InDeadband()
        {
            float gain = AudioSignalProcessor.ResolvePacketGain(capturedPeak: 1.0f, meterPeak: 0.50f, endpointGain: 1.0f, previousGain: 0.48f);

            Assert.Equal(0.48f, gain, 3);
        }

        [Fact]
        public void ComputePacketGainRamp_AttacksImmediately_OnSafetyDrivenGainDrop()
        {
            (float startGain, float gainStep) = AudioSignalProcessor.ComputePacketGainRamp(previousGain: 1.0f, packetGain: 0.4f, frames: 480, immediateAttack: true);

            Assert.Equal(0.4f, startGain, 3);
            Assert.Equal(0f, gainStep, 4);
        }

        [Fact]
        public void ComputePacketGainRamp_RampsOnNonSafetyGainDrop()
        {
            (float startGain, float gainStep) = AudioSignalProcessor.ComputePacketGainRamp(previousGain: 1.0f, packetGain: 0.4f, frames: 5, immediateAttack: false);

            Assert.Equal(1.0f, startGain, 3);
            Assert.Equal(-0.15f, gainStep, 3);
        }

        [Fact]
        public void ComputePacketGainRamp_RampsOnRelease()
        {
            (float startGain, float gainStep) = AudioSignalProcessor.ComputePacketGainRamp(previousGain: 0.4f, packetGain: 0.7f, frames: 5, immediateAttack: false);

            Assert.Equal(0.4f, startGain, 3);
            Assert.Equal(0.075f, gainStep, 3);
        }

        [Fact]
        public void SoftLimitSample_LeavesNormalSignalUnchanged()
        {
            float output = AudioSignalProcessor.SoftLimitSample(0.5f);

            Assert.Equal(0.5f, output, 4);
        }

        [Fact]
        public void SoftLimitSample_BendsPeaksWithoutCrossingCeiling()
        {
            float output = AudioSignalProcessor.SoftLimitSample(0.9f);

            Assert.InRange(output, 0.85f, 0.98f);
            Assert.True(output < 0.9f);
        }

        [Fact]
        public void SoftLimitSample_CapsExtremeOverload()
        {
            float positive = AudioSignalProcessor.SoftLimitSample(3.0f);
            float negative = AudioSignalProcessor.SoftLimitSample(-3.0f);

            Assert.InRange(positive, 0.95f, 0.98f);
            Assert.InRange(negative, -0.98f, -0.95f);
        }

        [Fact]
        public void SanitizeSample_RejectsInvalidValues()
        {
            Assert.Equal(0f, AudioSignalProcessor.SanitizeSample(float.NaN));
            Assert.Equal(0f, AudioSignalProcessor.SanitizeSample(float.PositiveInfinity));
            Assert.Equal(8f, AudioSignalProcessor.SanitizeSample(99f));
            Assert.Equal(-8f, AudioSignalProcessor.SanitizeSample(-99f));
        }
    }
}