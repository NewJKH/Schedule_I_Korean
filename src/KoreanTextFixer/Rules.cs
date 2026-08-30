using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace KoreanTextFixer
{
    // 정규식 번역 규칙(XUnity.AutoTranslator 문법).
    // 가격·이름·수량이 끼어드는 문장은 통짜 사전으로는 잡을 수 없어 이 규칙들이 담당한다.
    //
    //   r:"^The\ Laundromat\ \((.*?)\)$"=세탁소 ($1)     값은 그대로 끼워 넣는다
    //   sr:"Talk to ([a-zA-Z]+)$"=$1에게 말 걸기          값도 사전으로 번역해서 끼워 넣는다
    //
    // MelonLoader에서는 XUnity가 TextMeshPro를 후킹하지 못해 이 규칙들이 통째로 적용되지
    // 않았다. 그래서 직접 해석해 적용한다. 유니티에 의존하지 않으므로 따로 시험할 수 있다.
    internal static class Rules
    {
        internal sealed class Rule
        {
            internal Regex Pattern;
            internal string Replacement;
            internal bool TranslateGroups;   // sr: 이면 참
        }

        internal static readonly List<Rule> Direct = new List<Rule>();   // r:
        internal static readonly List<Rule> Split = new List<Rule>();    // sr:

        internal static int Count { get { return Direct.Count + Split.Count; } }

        // 사전 조회는 호출하는 쪽에서 넘겨준다 (Translations에 대한 의존을 만들지 않기 위해)
        internal static Func<string, string> Lookup = delegate { return null; };

        internal static void Add(string line)
        {
            bool split;
            int open;
            if (line.StartsWith("sr:\"")) { split = true; open = 4; }
            else if (line.StartsWith("r:\"")) { split = false; open = 3; }
            else return;

            int close = line.LastIndexOf("\"=", StringComparison.Ordinal);
            if (close <= open) return;

            string pattern = line.Substring(open, close - open);
            string replacement = Unescape(line.Substring(close + 2));
            if (replacement.Length == 0) return;

            try
            {
                var rule = new Rule
                {
                    Pattern = new Regex(pattern, RegexOptions.Singleline),
                    Replacement = replacement,
                    TranslateGroups = split
                };
                if (split) Split.Add(rule); else Direct.Add(rule);
            }
            catch
            {
                // 해석할 수 없는 규칙은 조용히 버린다. 하나 때문에 전체가 죽으면 안 된다.
            }
        }

        private static string Unescape(string s)
        {
            return s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\=", "=");
        }

        internal static string Apply(string src)
        {
            // 값을 그대로 두는 규칙이 더 구체적이므로 먼저 본다
            for (int i = 0; i < Direct.Count; i++)
            {
                Match m = Direct[i].Pattern.Match(src);
                if (!m.Success) continue;
                string result = Build(m, Direct[i].Replacement, false);
                if (result != null && result != src) return result;
            }
            for (int i = 0; i < Split.Count; i++)
            {
                Match m = Split[i].Pattern.Match(src);
                if (!m.Success) continue;
                string result = Build(m, Split[i].Replacement, true);
                if (result != null && result != src) return result;
            }
            return null;
        }

        // $1..$9 를 채워 넣는다. $$ 는 달러 기호 하나.
        // translateGroups(sr:)이면 각 조각을 사전으로 번역하고, 하나도 번역되지 않으면 포기한다.
        private static string Build(Match m, string replacement, bool translateGroups)
        {
            var sb = new StringBuilder(replacement.Length + 32);
            bool anyTranslated = false;

            for (int i = 0; i < replacement.Length; i++)
            {
                char c = replacement[i];
                if (c != '$' || i + 1 >= replacement.Length) { sb.Append(c); continue; }

                char next = replacement[i + 1];
                if (next == '$') { sb.Append('$'); i++; continue; }
                if (next < '1' || next > '9') { sb.Append(c); continue; }

                int g = next - '0';
                i++;
                if (g >= m.Groups.Count) continue;

                string part = m.Groups[g].Value;
                if (translateGroups && part.Length > 0)
                {
                    string t = Lookup(part);
                    if (t != null && t != part) { part = t; anyTranslated = true; }
                }
                sb.Append(part);
            }

            // 값까지 번역해야 의미가 있는 규칙인데 아무것도 못 바꿨다면 적용하지 않는다.
            // (예: "^([^\n]+)\n([\s\S]+)$" 같은 범용 분해 규칙이 원문을 그대로 되돌려놓는 것 방지)
            if (translateGroups && !anyTranslated) return null;
            return sb.ToString();
        }
    }
}
