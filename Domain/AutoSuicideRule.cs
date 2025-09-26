using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ToNRoundCounter.Domain
{
    /// <summary>
    /// Rule used to determine auto suicide behaviour based on round and terror expressions.
    /// Expressions support !, &&, || and parentheses.
    /// Parsing and evaluation now leverage LINQ recursion instead of hand written node trees.
    /// </summary>
    public class AutoSuicideRule
    {
        public string? Round { get; private set; }
        public bool RoundNegate { get; private set; }
        public string? Terror { get; private set; }
        public bool TerrorNegate { get; private set; }
        public string? RoundExpression { get; private set; }
        public string? TerrorExpression { get; private set; }
        public int Value { get; private set; }

        public static bool TryParse(string line, out AutoSuicideRule? rule)
        {
            rule = null;
            if (string.IsNullOrWhiteSpace(line)) return false;

            var parts = SplitEscaped(line);
            if (parts.Count == 1)
            {   // only value specified
                if (int.TryParse(parts[0], out var v) && (v == 0 || v == 1 || v == 2))
                {
                    rule = new AutoSuicideRule { Value = v };
                    return true;
                }
                return false;
            }
            if (parts.Count != 3) return false;
            if (!(int.TryParse(parts[2], out var value) && (value == 0 || value == 1 || value == 2)))
                return false;

            string? roundExpr = string.IsNullOrWhiteSpace(parts[0]) ? null : parts[0].Trim();
            string? terrorExpr = string.IsNullOrWhiteSpace(parts[1]) ? null : parts[1].Trim();

            bool roundNeg = false;
            if (!string.IsNullOrEmpty(roundExpr))
            {
                roundNeg = StripNegation(ref roundExpr);
                if (!string.IsNullOrEmpty(roundExpr) && !ValidateExpression(roundExpr)) return false;
            }

            bool terrorNeg = false;
            if (!string.IsNullOrEmpty(terrorExpr))
            {
                terrorNeg = StripNegation(ref terrorExpr);
                if (!string.IsNullOrEmpty(terrorExpr) && !ValidateExpression(terrorExpr)) return false;
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

        public static bool TryParseDetailed(string line, out AutoSuicideRule? rule, out string? error)
        {
            rule = null;
            error = null;
            if (string.IsNullOrWhiteSpace(line))
            {
                error = "形式が不正です";
                return false;
            }

            var parts = SplitEscaped(line);
            if (parts.Count == 1)
            {
                if (int.TryParse(parts[0], out var v) && (v == 0 || v == 1 || v == 2))
                {
                    rule = new AutoSuicideRule { Value = v };
                    return true;
                }
                error = "値が 0/1/2 以外";
                return false;
            }

            if (parts.Count != 3)
            {
                error = "セグメント数不正";
                return false;
            }

            if (!(int.TryParse(parts[2], out var value) && (value == 0 || value == 1 || value == 2)))
            {
                error = "値が 0/1/2 以外";
                return false;
            }

            string? roundExpr = string.IsNullOrWhiteSpace(parts[0]) ? null : parts[0].Trim();
            string? terrorExpr = string.IsNullOrWhiteSpace(parts[1]) ? null : parts[1].Trim();

            bool roundNeg = false;
            if (!string.IsNullOrEmpty(roundExpr))
            {
                roundNeg = StripNegation(ref roundExpr);
                if (!string.IsNullOrEmpty(roundExpr) && !ValidateExpression(roundExpr))
                {
                    error = "括弧の不整合や演算子の誤用";
                    return false;
                }
            }

            bool terrorNeg = false;
            if (!string.IsNullOrEmpty(terrorExpr))
            {
                terrorNeg = StripNegation(ref terrorExpr);
                if (!string.IsNullOrEmpty(terrorExpr) && !ValidateExpression(terrorExpr))
                {
                    error = "括弧の不整合や演算子の誤用";
                    return false;
                }
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

        public bool Matches(string? round, string? terror, Func<string, string, bool> comparer)
        {
            bool roundMatch = RoundExpression == null ? true : (RoundNegate ? !Evaluate(RoundExpression, round, comparer) : Evaluate(RoundExpression, round, comparer));
            bool terrorMatch = TerrorExpression == null ? true : (TerrorNegate ? !Evaluate(TerrorExpression, terror, comparer) : Evaluate(TerrorExpression, terror, comparer));
            return roundMatch && terrorMatch;
        }

        public bool MatchesRound(string? round, Func<string, string, bool> comparer)
        {
            if (RoundExpression == null)
                return true;
            bool match = Evaluate(RoundExpression, round, comparer);
            return RoundNegate ? !match : match;
        }

        public bool Covers(AutoSuicideRule other)
        {
            // Determine if this rule's conditions cover another rule's conditions.
            // Later rules override earlier ones; if this rule matches every scenario
            // that the other rule matches, the other is redundant.  Rather than rely
            // on enums of all possible rounds and terrors, evaluate only the terms
            // present in the other rule's expressions.
            Func<string, string, bool> comparer = (a, b) => a == b;

            var roundTerms = other.GetRoundTerms();
            var roundCandidates = roundTerms?.Select(r => (string?)r).ToList() ?? new List<string?> { null };
            if (other.RoundNegate && !roundCandidates.Contains(null))
                roundCandidates.Add(null);

            var terrorTerms = other.GetTerrorTerms();
            var terrorCandidates = terrorTerms?.Select(t => (string?)t).ToList() ?? new List<string?> { null };
            if (other.TerrorNegate && !terrorCandidates.Contains(null))
                terrorCandidates.Add(null);

            foreach (var round in roundCandidates)
            {
                foreach (var terror in terrorCandidates)
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
            return $"{EscapeSegment(r)}:{EscapeSegment(t)}:{Value}";
        }

        /// <summary>
        /// Returns a list of simple round terms when the round expression is a
        /// disjunction (A||B||...).  Parentheses wrapping the expression are
        /// ignored.  If the expression cannot be represented as such a list,
        /// null is returned.
        /// </summary>
        public List<string>? GetRoundTerms()
        {
            return GetSimpleTerms(RoundExpression);
        }

        /// <summary>
        /// Returns a list of simple terror terms when the terror expression is a
        /// disjunction (A||B||...).  Parentheses wrapping the expression are
        /// ignored.  If the expression cannot be represented as such a list,
        /// null is returned.
        /// </summary>
        public List<string>? GetTerrorTerms()
        {
            return GetSimpleTerms(TerrorExpression);
        }

        private static List<string>? GetSimpleTerms(string? expr)
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

        private static bool StripNegation(ref string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr))
                return false;

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

            int depth = 0;
            bool expectTerm = true;

            for (int i = 0; i < expr.Length; i++)
            {
                char c = expr[i];

                if (char.IsWhiteSpace(c))
                    continue;

                if (c == '(')
                {
                    if (!expectTerm) return false;
                    depth++;
                    expectTerm = true;
                }
                else if (c == ')')
                {
                    if (depth == 0 || expectTerm) return false;
                    depth--;
                    expectTerm = false;
                }
                else if (c == '!')
                {
                    if (!expectTerm) return false;
                }
                else if (c == '&' || c == '|')
                {
                    if (i + 1 >= expr.Length || expr[i + 1] != c) return false; // single & or |
                    if (expectTerm) return false;
                    expectTerm = true;
                    i++; // skip next char
                }
                else
                {
                    // read identifier token
                    int start = i;
                    while (i < expr.Length && !char.IsWhiteSpace(expr[i]) && expr[i] != '!' && expr[i] != '&' && expr[i] != '|' && expr[i] != '(' && expr[i] != ')')
                        i++;
                    if (start == i) return false;
                    i--; // compensate for for-loop increment
                    expectTerm = false;
                }
            }

            return depth == 0 && !expectTerm;
        }

        private static bool Evaluate(string expr, string? input, Func<string, string, bool> comparer)
        {
            if (string.IsNullOrWhiteSpace(expr)) return true;
            expr = expr.Trim();
            if (expr.StartsWith("(") && MatchingParen(expr) == expr.Length - 1)
                return Evaluate(expr.Substring(1, expr.Length - 2), input, comparer);
            var orParts = SplitTopLevel(expr, "||").ToList();
            if (orParts.Count > 1)
                return orParts.Any(p => Evaluate(p, input, comparer));
            var andParts = SplitTopLevel(expr, "&&").ToList();
            if (andParts.Count > 1)
                return andParts.All(p => Evaluate(p, input, comparer));
            if (expr.StartsWith("!"))
                return !Evaluate(expr.Substring(1), input, comparer);
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

        private static bool IsSimple(string? expr)
        {
            if (string.IsNullOrEmpty(expr)) return false;
            return !(expr.Contains("&&") || expr.Contains("||") || expr.Contains("!") || expr.Contains("(") || expr.Contains(")"));
        }

        private static List<string> SplitEscaped(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool escape = false;
            foreach (var c in line)
            {
                if (escape)
                {
                    sb.Append(c);
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == ':')
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            if (escape) sb.Append('\\');
            result.Add(sb.ToString());
            return result;
        }

        private static string? EscapeSegment(string? s)
        {
            if (s == null) return null;
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                if (c == ':' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
