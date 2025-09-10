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

        private ConditionNode roundCondition;
        private ConditionNode terrorCondition;
        private string roundExpr;
        private string terrorExpr;

        private abstract class ConditionNode
        {
            public abstract bool Evaluate(string input, Func<string, string, bool> comparer);
        }

        private class ValueNode : ConditionNode
        {
            public string Value { get; }
            public ValueNode(string value) { Value = value; }
            public override bool Evaluate(string input, Func<string, string, bool> comparer)
            {
                if (input == null) return false;
                return comparer(input, Value);
            }
        }

        private class AndNode : ConditionNode
        {
            public ConditionNode Left { get; }
            public ConditionNode Right { get; }
            public AndNode(ConditionNode left, ConditionNode right)
            {
                Left = left; Right = right;
            }
            public override bool Evaluate(string input, Func<string, string, bool> comparer)
            {
                return Left.Evaluate(input, comparer) && Right.Evaluate(input, comparer);
            }
        }

        private class OrNode : ConditionNode
        {
            public ConditionNode Left { get; }
            public ConditionNode Right { get; }
            public OrNode(ConditionNode left, ConditionNode right)
            {
                Left = left; Right = right;
            }
            public override bool Evaluate(string input, Func<string, string, bool> comparer)
            {
                return Left.Evaluate(input, comparer) || Right.Evaluate(input, comparer);
            }
        }

        private class NotNode : ConditionNode
        {
            public ConditionNode Inner { get; }
            public NotNode(ConditionNode inner)
            {
                Inner = inner;
            }
            public override bool Evaluate(string input, Func<string, string, bool> comparer)
            {
                return !Inner.Evaluate(input, comparer);
            }
        }

        public static bool TryParse(string line, out AutoSuicideRule rule)
        {
            rule = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            var parts = line.Split(':');
            string round = null;
            bool roundNeg = false;
            string terror = null;
            bool terrorNeg = false;
            ConditionNode roundCond = null;
            ConditionNode terrorCond = null;
            string roundExprStr = null;
            string terrorExprStr = null;
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
                    var tmp = roundNeg ? parts[0].Substring(1) : parts[0];
                    if (!TryParseCondition(tmp, out roundCond)) return false;
                    if (roundCond is ValueNode) round = tmp;
                    roundExprStr = tmp;
                }
                if (!string.IsNullOrWhiteSpace(parts[1]))
                {
                    terrorNeg = parts[1].StartsWith("!");
                    var tmp = terrorNeg ? parts[1].Substring(1) : parts[1];
                    if (!TryParseCondition(tmp, out terrorCond)) return false;
                    if (terrorCond is ValueNode) terror = tmp;
                    terrorExprStr = tmp;
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
            rule = new AutoSuicideRule { Round = round, RoundNegate = roundNeg, Terror = terror, TerrorNegate = terrorNeg, Value = value, roundCondition = roundCond, terrorCondition = terrorCond, roundExpr = roundExprStr, terrorExpr = terrorExprStr };
            return true;
        }

        public static bool TryParseDetailed(string line, out AutoSuicideRule rule, out string error)
        {
            rule = null;
            error = null;
            if (string.IsNullOrWhiteSpace(line))
            {
                error = "空行です";
                return false;
            }
            var parts = line.Split(':');
            string round = null;
            bool roundNeg = false;
            string terror = null;
            bool terrorNeg = false;
            ConditionNode roundCond = null;
            ConditionNode terrorCond = null;
            string roundExprStr = null;
            string terrorExprStr = null;
            int value;
            if (parts.Length == 1)
            {
                if (parts[0] == "1" || parts[0] == "0" || parts[0] == "2")
                {
                    value = int.Parse(parts[0]);
                }
                else
                {
                    error = $"値 '{parts[0]}' が不正です。0,1,2のみ使用できます";
                    return false;
                }
            }
            else if (parts.Length == 3)
            {
                if (!string.IsNullOrWhiteSpace(parts[0]))
                {
                    roundNeg = parts[0].StartsWith("!");
                    var tmp = roundNeg ? parts[0].Substring(1) : parts[0];
                    if (!TryParseCondition(tmp, out roundCond, out error))
                    {
                        error = $"ラウンド指定 '{tmp}' が不正です: {error}";
                        return false;
                    }
                    if (roundCond is ValueNode) round = tmp;
                    roundExprStr = tmp;
                }
                if (!string.IsNullOrWhiteSpace(parts[1]))
                {
                    terrorNeg = parts[1].StartsWith("!");
                    var tmp = terrorNeg ? parts[1].Substring(1) : parts[1];
                    if (!TryParseCondition(tmp, out terrorCond, out error))
                    {
                        error = $"テラー指定 '{tmp}' が不正です: {error}";
                        return false;
                    }
                    if (terrorCond is ValueNode) terror = tmp;
                    terrorExprStr = tmp;
                }
                if (parts[2] == "1" || parts[2] == "0" || parts[2] == "2")
                {
                    value = int.Parse(parts[2]);
                }
                else
                {
                    error = $"値 '{parts[2]}' が不正です。0,1,2のみ使用できます";
                    return false;
                }
            }
            else
            {
                error = "形式が不正です。'ラウンド:テラー:値' または '値' の形式で記述してください";
                return false;
            }
            rule = new AutoSuicideRule { Round = round, RoundNegate = roundNeg, Terror = terror, TerrorNegate = terrorNeg, Value = value, roundCondition = roundCond, terrorCondition = terrorCond, roundExpr = roundExprStr, terrorExpr = terrorExprStr };
            return true;
        }

        public bool Matches(string round, string terror, Func<string, string, bool> comparer)
        {
            bool roundMatch;
            if (roundCondition != null)
            {
                bool res = roundCondition.Evaluate(round, comparer);
                roundMatch = RoundNegate ? !res : res;
            }
            else
            {
                roundMatch = Round == null || (round != null && (RoundNegate ? !comparer(round, Round) : comparer(round, Round)));
            }
            bool terrorMatch;
            if (terrorCondition != null)
            {
                bool res = terrorCondition.Evaluate(terror, comparer);
                terrorMatch = TerrorNegate ? !res : res;
            }
            else
            {
                terrorMatch = Terror == null || (terror != null && (TerrorNegate ? !comparer(terror, Terror) : comparer(terror, Terror)));
            }
            return roundMatch && terrorMatch;
        }

        public bool Covers(AutoSuicideRule other)
        {
            if (roundCondition != null || other.roundCondition != null || terrorCondition != null || other.terrorCondition != null)
                return false;
            bool roundCovers = (Round == null && !RoundNegate) || (other.Round != null && Round == other.Round && RoundNegate == other.RoundNegate);
            bool terrorCovers = (Terror == null && !TerrorNegate) || (other.Terror != null && Terror == other.Terror && TerrorNegate == other.TerrorNegate);
            return roundCovers && terrorCovers;
        }

        public override string ToString()
        {
            if (roundExpr == null && terrorExpr == null)
            {
                return Value.ToString();
            }
            string r = roundExpr == null ? "" : (RoundNegate ? "!" + roundExpr : roundExpr);
            string t = terrorExpr == null ? "" : (TerrorNegate ? "!" + terrorExpr : terrorExpr);
            return $"{r}:{t}:{Value}";
        }

        private static bool TryParseCondition(string expr, out ConditionNode node)
        {
            string dummy;
            return TryParseCondition(expr, out node, out dummy);
        }

        private static bool TryParseCondition(string expr, out ConditionNode node, out string error)
        {
            node = null;
            error = null;
            if (expr == null)
                return true;
            expr = expr.Trim();
            if (expr.Length == 0)
            {
                error = "空の条件です";
                return false;
            }
            if (expr.StartsWith("(") && FindMatchingParen(expr, 0) == expr.Length - 1)
            {
                return TryParseCondition(expr.Substring(1, expr.Length - 2), out node, out error);
            }
            int depth = 0;
            var andOps = new System.Collections.Generic.List<int>();
            var orOps = new System.Collections.Generic.List<int>();
            for (int i = 0; i < expr.Length - 1; i++)
            {
                char c = expr[i];
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                    if (depth < 0)
                    {
                        error = "括弧の対応が取れていません";
                        return false;
                    }
                }
                if (depth == 0)
                {
                    if (c == '&' && expr[i + 1] == '&')
                    {
                        andOps.Add(i);
                        i++;
                    }
                    else if (c == '|' && expr[i + 1] == '|')
                    {
                        orOps.Add(i);
                        i++;
                    }
                }
            }
            if (depth != 0)
            {
                error = "括弧の対応が取れていません";
                return false;
            }
            if (andOps.Count > 0 && orOps.Count > 0)
            {
                error = "&&と||が混在しています";
                return false;
            }
            if (andOps.Count > 0)
            {
                var parts = new System.Collections.Generic.List<ConditionNode>();
                int start = 0;
                foreach (var idx in andOps)
                {
                    var sub = expr.Substring(start, idx - start);
                    if (!TryParseCondition(sub, out var subNode, out error)) return false;
                    parts.Add(subNode);
                    start = idx + 2;
                }
                var last = expr.Substring(start);
                if (!TryParseCondition(last, out var lastNode, out error)) return false;
                parts.Add(lastNode);
                node = parts[0];
                for (int i = 1; i < parts.Count; i++)
                    node = new AndNode(node, parts[i]);
                return true;
            }
            if (orOps.Count > 0)
            {
                var parts = new System.Collections.Generic.List<ConditionNode>();
                int start = 0;
                foreach (var idx in orOps)
                {
                    var sub = expr.Substring(start, idx - start);
                    if (!TryParseCondition(sub, out var subNode, out error)) return false;
                    parts.Add(subNode);
                    start = idx + 2;
                }
                var last = expr.Substring(start);
                if (!TryParseCondition(last, out var lastNode, out error)) return false;
                parts.Add(lastNode);
                node = parts[0];
                for (int i = 1; i < parts.Count; i++)
                    node = new OrNode(node, parts[i]);
                return true;
            }
            if (expr.StartsWith("!"))
            {
                var sub = expr.Substring(1);
                if (!TryParseCondition(sub, out var inner, out error)) return false;
                node = new NotNode(inner);
                return true;
            }
            node = new ValueNode(expr.Trim());
            return true;
        }

        private static int FindMatchingParen(string s, int start)
        {
            int depth = 0;
            for (int i = start; i < s.Length; i++)
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
    }
}
