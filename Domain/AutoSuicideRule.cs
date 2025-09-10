using System;
using System.Collections.Generic;
using System.Linq;

namespace ToNRoundCounter.Domain
{
    /// <summary>
    /// Rule used to determine auto suicide behaviour based on round and terror expressions.
    /// Expressions support !, &&, || and parentheses.
    /// Parsing and evaluation now leverage LINQ recursion instead of hand written node trees.
    /// </summary>
    public class AutoSuicideRule
    {
        public string Round { get; private set; }
        public bool RoundNegate { get; private set; }
        public string Terror { get; private set; }
        public bool TerrorNegate { get; private set; }
        public string RoundExpression { get; private set; }
        public string TerrorExpression { get; private set; }
        public int Value { get; private set; }

        public static bool TryParse(string line, out AutoSuicideRule rule)
        {
            rule = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            var parts = line.Split(':');
            if (parts.Length == 1)
            {   // only value specified
                if (int.TryParse(parts[0], out var v) && (v == 0 || v == 1 || v == 2))
                {
                    rule = new AutoSuicideRule { Value = v };
                    return true;
                }
                return false;
            }
            if (parts.Length != 3) return false;
            if (!(int.TryParse(parts[2], out var value) && (value == 0 || value == 1 || value == 2)))
                return false;

            string roundExpr = string.IsNullOrWhiteSpace(parts[0]) ? null : parts[0].Trim();
            string terrorExpr = string.IsNullOrWhiteSpace(parts[1]) ? null : parts[1].Trim();

            bool roundNeg = false;
            if (!string.IsNullOrEmpty(roundExpr))
            {
                roundNeg = StripNegation(ref roundExpr);
                if (!ValidateExpression(roundExpr)) return false;
            }

            bool terrorNeg = false;
            if (!string.IsNullOrEmpty(terrorExpr))
            {
                terrorNeg = StripNegation(ref terrorExpr);
                if (!ValidateExpression(terrorExpr)) return false;
            }

            rule = new AutoSuicideRule
            {
                RoundExpression = roundExpr,
                TerrorExpression = terrorExpr,
                RoundNegate = roundNeg,
                TerrorNegate = terrorNeg,
                Round = IsSimple(roundExpr) ? roundExpr : null,
                Terror = IsSimple(terrorExpr) ? terrorExpr : null,
                Value = value
            };
            return true;
        }

        public static bool TryParseDetailed(string line, out AutoSuicideRule rule, out string error)
        {
            rule = null;
            error = null;
            if (!TryParse(line, out rule))
            {
                error = "形式が不正です";
                return false;
            }
            return true;
        }

        public bool Matches(string round, string terror, Func<string, string, bool> comparer)
        {
            bool roundMatch = RoundExpression == null ? true : (RoundNegate ? !Evaluate(RoundExpression, round, comparer) : Evaluate(RoundExpression, round, comparer));
            bool terrorMatch = TerrorExpression == null ? true : (TerrorNegate ? !Evaluate(TerrorExpression, terror, comparer) : Evaluate(TerrorExpression, terror, comparer));
            return roundMatch && terrorMatch;
        }

        public bool Covers(AutoSuicideRule other)
        {
            // Determine if this rule's conditions cover another rule's conditions.
            // Later rules override earlier ones; if this rule matches every scenario
            // that the other rule matches, the other is redundant.  Rather than rely
            // on enums of all possible rounds and terrors, evaluate only the terms
            // present in the other rule's expressions.
            Func<string, string, bool> comparer = (a, b) => a == b;

            var roundTerms = other.GetRoundTerms() ?? new List<string> { null };
            var terrorTerms = other.GetTerrorTerms() ?? new List<string> { null };

            foreach (var round in roundTerms)
            {
                foreach (var terror in terrorTerms)
                {
                    bool thisMatches = Matches(round, terror, comparer);
                    bool otherMatches = other.Matches(round, terror, comparer);
                    if (otherMatches && !thisMatches)
                        return false;
                }
            }
            return true;
        }

        public override string ToString()
        {
            if (RoundExpression == null && TerrorExpression == null)
                return Value.ToString();
            string r = RoundExpression == null ? "" : (RoundNegate ? "!" + RoundExpression : RoundExpression);
            string t = TerrorExpression == null ? "" : (TerrorNegate ? "!" + TerrorExpression : TerrorExpression);
            return $"{r}:{t}:{Value}";
        }

        /// <summary>
        /// Returns a list of simple round terms when the round expression is a
        /// disjunction (A||B||...).  Parentheses wrapping the expression are
        /// ignored.  If the expression cannot be represented as such a list,
        /// null is returned.
        /// </summary>
        public List<string> GetRoundTerms()
        {
            return GetSimpleTerms(RoundExpression);
        }

        /// <summary>
        /// Returns a list of simple terror terms when the terror expression is a
        /// disjunction (A||B||...).  Parentheses wrapping the expression are
        /// ignored.  If the expression cannot be represented as such a list,
        /// null is returned.
        /// </summary>
        public List<string> GetTerrorTerms()
        {
            return GetSimpleTerms(TerrorExpression);
        }

        private static List<string> GetSimpleTerms(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return null;
            string working = expr.Trim();
            if (working.StartsWith("(") && MatchingParen(working) == working.Length - 1)
                working = working.Substring(1, working.Length - 2);
            var parts = SplitTopLevel(working, "||").ToList();
            if (parts.Count > 1 && parts.All(p => IsSimple(p.Trim())))
                return parts.Select(p => p.Trim()).ToList();
            if (IsSimple(working))
                return new List<string> { working };
            return null;
        }

        private static bool StripNegation(ref string expr)
        {
            expr = expr.Trim();
            if (!expr.StartsWith("!")) return false;
            string candidate = expr.Substring(1).Trim();
            if (candidate.StartsWith("(") && MatchingParen(candidate) == candidate.Length - 1)
            {
                expr = candidate;
                return true;
            }
            if (IsSimple(candidate))
            {
                expr = candidate;
                return true;
            }
            return false;
        }

        private static bool ValidateExpression(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return true;
            try
            {
                Evaluate(expr, "dummy", (a, b) => a == b);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool Evaluate(string expr, string input, Func<string, string, bool> comparer)
        {
            if (string.IsNullOrWhiteSpace(expr)) return true;
            expr = expr.Trim();
            if (expr.StartsWith("!"))
                return !Evaluate(expr.Substring(1), input, comparer);
            if (expr.StartsWith("(") && MatchingParen(expr) == expr.Length - 1)
                return Evaluate(expr.Substring(1, expr.Length - 2), input, comparer);
            var orParts = SplitTopLevel(expr, "||").ToList();
            if (orParts.Count > 1)
                return orParts.Any(p => Evaluate(p, input, comparer));
            var andParts = SplitTopLevel(expr, "&&").ToList();
            if (andParts.Count > 1)
                return andParts.All(p => Evaluate(p, input, comparer));
            return input != null && comparer(input, expr);
        }

        private static IEnumerable<string> SplitTopLevel(string expr, string op)
        {
            int depth = 0;
            int start = 0;
            for (int i = 0; i <= expr.Length - op.Length; i++)
            {
                char c = expr[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                if (depth == 0 && expr.Substring(i, op.Length) == op)
                {
                    yield return expr.Substring(start, i - start);
                    start = i + op.Length;
                    i += op.Length - 1;
                }
            }
            yield return expr.Substring(start);
        }

        private static int MatchingParen(string s)
        {
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static bool IsSimple(string expr)
        {
            if (string.IsNullOrEmpty(expr)) return false;
            return !(expr.Contains("&&") || expr.Contains("||") || expr.Contains("!") || expr.Contains("(") || expr.Contains(")"));
        }
    }
}
