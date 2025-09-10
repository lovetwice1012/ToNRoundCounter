using System;

namespace ToNRoundCounter.Domain
{
    /// <summary>
    /// Value object representing a unique identifier for a round.
    /// </summary>
    public readonly struct RoundId : IEquatable<RoundId>
    {
        public RoundId(Guid value)
        {
            Value = value;
        }

        public Guid Value { get; }

        public bool Equals(RoundId other) => Value.Equals(other.Value);

        public override bool Equals(object obj) => obj is RoundId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString();

        public static bool operator ==(RoundId left, RoundId right) => left.Equals(right);
        public static bool operator !=(RoundId left, RoundId right) => !left.Equals(right);
    }
}

