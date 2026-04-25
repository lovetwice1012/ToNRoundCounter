#nullable enable

using System;

namespace ToNRoundCounter.Application.Recording
{
    internal static class AudioSignalProcessor
    {
        private const float UnityGain = 1.0f;
        private const float MinimumGain = 0.0f;
        private const float MeterPeakFloor = 0.0001f;
        private const float PeakSafetyCeiling = 0.80f;
        private const float GlobalOutputGainCeiling = 0.50f;
        private const float GainDeadband = 0.03f;
        private const float AttackSmoothingFactor = 0.35f;
        private const float ReleaseSmoothingFactor = 0.1f;
        private const float SoftLimitKneeStart = 0.85f;
        private const float SoftLimitCeiling = 0.98f;
        private const float AbsoluteCeiling = 8f;

        public static float ResolvePacketGain(float capturedPeak, float? meterPeak, float endpointGain, float previousGain)
        {
            return ResolvePacketGain(capturedPeak, meterPeak, endpointGain, previousGain, out _);
        }

        public static float ResolvePacketGain(float capturedPeak, float? meterPeak, float endpointGain, float previousGain, out bool immediateAttack)
        {
            float loudnessTarget = ComputeLoudnessTargetGain(capturedPeak, meterPeak, endpointGain);
            float smoothedGain = SmoothGain(previousGain, loudnessTarget);

            // Safety is hard-capped so hot packets can never pass through unchanged,
            // even while loudness-following smoothing is active.
            float safetyGain = ComputeSafetyGain(capturedPeak);
            immediateAttack = safetyGain + MeterPeakFloor < previousGain;
            return Math.Min(smoothedGain, safetyGain);
        }

        public static float ComputeTargetGain(float capturedPeak, float? meterPeak, float endpointGain)
        {
            float targetGain = ComputeLoudnessTargetGain(capturedPeak, meterPeak, endpointGain);
            return Math.Min(targetGain, ComputeSafetyGain(capturedPeak));
        }

        public static (float startGain, float gainStep) ComputePacketGainRamp(float previousGain, float packetGain, int frames, bool immediateAttack)
        {
            if (frames <= 1)
            {
                return (packetGain, 0f);
            }

            if (packetGain < previousGain)
            {
                if (immediateAttack)
                {
                    // Safety attenuation is immediate so we never let the packet head clip
                    // before the lower gain takes effect.
                    return (packetGain, 0f);
                }

                // Non-safety attenuation is ramped to avoid packet-boundary zipper noise.
                float attenuationStep = (packetGain - previousGain) / (frames - 1);
                return (previousGain, attenuationStep);
            }

            if (packetGain == previousGain)
            {
                return (packetGain, 0f);
            }

            float gainStep = (packetGain - previousGain) / (frames - 1);
            return (previousGain, gainStep);
        }

        public static float SoftLimitSample(float value)
        {
            value = SanitizeSample(value);

            float magnitude = Math.Abs(value);
            if (magnitude <= SoftLimitKneeStart)
            {
                return value;
            }

            float excess = magnitude - SoftLimitKneeStart;
            float shaped = SoftLimitKneeStart + (excess / (1f + (excess / (SoftLimitCeiling - SoftLimitKneeStart))));
            if (shaped > magnitude)
            {
                shaped = magnitude;
            }
            if (shaped > SoftLimitCeiling)
            {
                shaped = SoftLimitCeiling;
            }

            return MathF.CopySign(shaped, value);
        }

        public static float SanitizeSample(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            if (value > AbsoluteCeiling)
            {
                return AbsoluteCeiling;
            }

            if (value < -AbsoluteCeiling)
            {
                return -AbsoluteCeiling;
            }

            return value;
        }

        private static float ComputeLoudnessTargetGain(float capturedPeak, float? meterPeak, float endpointGain)
        {
            float normalizedEndpointGain = NormalizeGain(endpointGain);
            float targetGain = normalizedEndpointGain;
            if (capturedPeak > MeterPeakFloor && meterPeak.HasValue && IsFinitePositive(meterPeak.Value))
            {
                targetGain = meterPeak.Value / capturedPeak;
            }

            if (float.IsNaN(targetGain) || float.IsInfinity(targetGain))
            {
                return normalizedEndpointGain;
            }

            targetGain = NormalizeGain(targetGain);

            // Never allow meter-derived gain to exceed the endpoint's own attenuation.
            // This prevents recordings from becoming louder than what users actually hear.
            if (targetGain > normalizedEndpointGain)
            {
                targetGain = normalizedEndpointGain;
            }

            // Enforce a global output ceiling for user safety.
            if (targetGain > GlobalOutputGainCeiling)
            {
                targetGain = GlobalOutputGainCeiling;
            }

            return targetGain;
        }

        private static float ComputeSafetyGain(float capturedPeak)
        {
            if (capturedPeak <= MeterPeakFloor)
            {
                return UnityGain;
            }

            float overloadSafeGain = PeakSafetyCeiling / capturedPeak;
            if (!IsFinitePositive(overloadSafeGain))
            {
                return UnityGain;
            }

            if (overloadSafeGain < MinimumGain)
            {
                return MinimumGain;
            }

            if (overloadSafeGain > UnityGain)
            {
                return UnityGain;
            }

            return overloadSafeGain;
        }

        private static float SmoothGain(float previousGain, float targetGain)
        {
            previousGain = NormalizeGain(previousGain);

            float delta = targetGain - previousGain;
            if (Math.Abs(delta) <= GainDeadband)
            {
                return previousGain;
            }

            float smoothingFactor = delta < 0f ? AttackSmoothingFactor : ReleaseSmoothingFactor;
            return previousGain + (delta * smoothingFactor);
        }

        private static float NormalizeGain(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return UnityGain;
            }

            if (value < MinimumGain)
            {
                return MinimumGain;
            }

            if (value > UnityGain)
            {
                return UnityGain;
            }

            return value;
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > MeterPeakFloor;
        }
    }
}