using System;

namespace ToNRoundCounter.Models
{
    public class AutoSuicideRule
    {
        public string Round { get; set; }
        public bool RoundNegate { get; set; }
        public string Terror { get; set; }
        public bool TerrorNegate { get; set; }
        public int Value { get; set; }

        public static bool TryParse(string line, out AutoSuicideRule rule)
        {
            rule = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            var parts = line.Split(':');
            string round = null;
            bool roundNeg = false;
            string terror = null;
            bool terrorNeg = false;
            int value;
            if (parts.Length == 1)
            {
                if (parts[0] == "1" || parts[0] == "0" || parts[0] == "2")
                {
                    value = int.Parse(parts[0]);
                }
                else
                {
                    return false;
                }
            }
            else if (parts.Length == 3)
            {
                if (!string.IsNullOrWhiteSpace(parts[0]))
                {
                    roundNeg = parts[0].StartsWith("!");
                    round = roundNeg ? parts[0].Substring(1) : parts[0];
                }
                if (!string.IsNullOrWhiteSpace(parts[1]))
                {
                    terrorNeg = parts[1].StartsWith("!");
                    terror = terrorNeg ? parts[1].Substring(1) : parts[1];
                }
                if (parts[2] == "1" || parts[2] == "0" || parts[2] == "2")
                {
                    value = int.Parse(parts[2]);
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
            rule = new AutoSuicideRule { Round = round, RoundNegate = roundNeg, Terror = terror, TerrorNegate = terrorNeg, Value = value };
            return true;
        }

        public bool Matches(string round, string terror, Func<string, string, bool> comparer)
        {
            bool roundMatch = Round == null || (round != null && (RoundNegate ? !comparer(round, Round) : comparer(round, Round)));
            bool terrorMatch = Terror == null || (terror != null && (TerrorNegate ? !comparer(terror, Terror) : comparer(terror, Terror)));
            return roundMatch && terrorMatch;
        }

        public bool Covers(AutoSuicideRule other)
        {
            bool roundCovers = (Round == null && !RoundNegate) || (other.Round != null && Round == other.Round && RoundNegate == other.RoundNegate);
            bool terrorCovers = (Terror == null && !TerrorNegate) || (other.Terror != null && Terror == other.Terror && TerrorNegate == other.TerrorNegate);
            return roundCovers && terrorCovers;
        }

        public override string ToString()
        {
            if (Round == null && Terror == null)
            {
                return Value.ToString();
            }
            string r = Round == null ? "" : (RoundNegate ? "!" + Round : Round);
            string t = Terror == null ? "" : (TerrorNegate ? "!" + Terror : Terror);
            return $"{r}:{t}:{Value}";
        }
    }
}
