using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KoreanTextFixer
{
    // 게임이 실제로 화면에 쓴 문구 중 번역이 없는 것만 모아 파일로 남긴다.
    //
    // 게임 파일을 정적으로 훑는 방식으로는 화면에 나오는 문구와 에셋 이름을 구분할 수 없다
    // (실측 결과 후보 29,803개 중 5,450개가 머티리얼·오브젝트 이름 같은 노이즈였다).
    // 후킹은 이미 표시되는 문구를 전부 보고 있으므로, 여기서 걸러 모으는 편이 정확하다.
    internal static class MissingLog
    {
        private const int Limit = 3000;          // 파일이 무한정 커지지 않게
        private const int FlushEvery = 20;       // 이만큼 쌓이면 파일에 붙인다

        private static readonly HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<string> Pending = new List<string>(FlushEvery);
        private static string _path;
        private static bool _enabled;

        internal static void Enable(string path)
        {
            _path = path;
            _enabled = true;
            try
            {
                if (!File.Exists(_path))
                {
                    File.WriteAllText(_path,
                        "// 번역이 없는 문구 목록 (게임이 실제로 화면에 쓴 것만)" + Environment.NewLine +
                        "// 줄바꿈은 \\n 으로 적혀 있다. 이 줄들을 번역해 Korean_Base.txt 에 넣으면 된다." + Environment.NewLine,
                        Encoding.UTF8);
                }
                KLog.Info("미번역 수집: " + _path);
            }
            catch (Exception e)
            {
                _enabled = false;
                KLog.Warn("미번역 수집을 켤 수 없습니다: " + e.Message);
            }
        }

        internal static void Record(string src)
        {
            if (!_enabled || Seen.Count >= Limit) return;
            if (src.Length < 3 || src.Length > 500) return;
            if (IsUserInput(src) || IsNoise(src)) return;
            if (!Seen.Add(src)) return;

            Pending.Add(src.Replace("\r", "").Replace("\n", "\\n"));
            if (Pending.Count >= FlushEvery) Flush();
        }

        internal static void Flush()
        {
            if (!_enabled || Pending.Count == 0) return;
            try
            {
                File.AppendAllLines(_path, Pending, Encoding.UTF8);
            }
            catch
            {
                // 파일을 쓸 수 없으면 조용히 포기한다. 번역 자체는 계속 돌아야 한다.
            }
            Pending.Clear();
        }

        internal static int Count { get { return Seen.Count; } }

        // 번역 대상이 아닌 것이 매 세션 반복 수집되면 파일만 어지럽힌다.
        // 키캡 표기(Esc), 공급자 이니셜(S.W), 개발용 자리 문구가 실측상 반복 수집됐다.
        private static bool IsNoise(string s)
        {
            if (s == "Esc" || s == "Tab" || s == "Del" || s == "End" || s == "Ins") return true;
            if (s.Length == 3 && s[1] == '.' && char.IsUpper(s[0]) && char.IsUpper(s[2])) return true;
            if (s.StartsWith("Lorem ipsum", StringComparison.Ordinal)) return true;
            if (s.StartsWith("2x Item Name", StringComparison.Ordinal)) return true;
            return false;
        }

        // 플레이어가 입력창에 치고 있는 글자는 번역 대상이 아니고, 파일에 남겨서도 안 된다.
        // 입력 중에는 캐럿 표시용 폭 없는 문자가 섞여 들어오고, 글자 수도 한두 개다.
        private static bool IsUserInput(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '​' || c == '‌' || c == '﻿') return true;
            }
            int letters = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsLetter(s[i])) letters++;
            }
            return letters < 2;
        }
    }
}
