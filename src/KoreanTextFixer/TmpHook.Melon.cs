#if MELON
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppTMPro;

namespace KoreanTextFixer
{
    // MelonLoader의 Il2CppInterop은 비유니티 네임스페이스에 Il2Cpp 접두사를 붙인다(TMPro -> Il2CppTMPro).
    // XUnity.AutoTranslator는 "TMPro.TMP_Text" 라는 이름으로 타입을 찾기 때문에 MelonLoader에서는
    // TextMeshPro 후킹을 통째로 건너뛴다. 그래서 TMP 텍스트가 폴링으로만 번역되어
    // 표시 후 1초쯤 뒤에 바뀌거나 깜박였다.
    // 여기서 직접 set_text / SetText 를 후킹해 값이 쓰이기 직전에 번역해 넘긴다.
    internal static class TmpHook
    {
        internal static bool Installed;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            var prefix = new HarmonyMethod(typeof(TmpHook).GetMethod(
                nameof(TextPrefix), BindingFlags.NonPublic | BindingFlags.Static));

            int patched = 0;
            foreach (MethodBase target in Targets())
            {
                try
                {
                    harmony.Patch(target, prefix);
                    patched++;
                    KLog.Info("hooked " + target.DeclaringType.Name + "." + target.Name);
                }
                catch (Exception e)
                {
                    KLog.Warn("hook failed: " + target.Name + " - " + e.Message);
                }
            }
            Installed = patched > 0;
            if (!Installed) KLog.Warn("TMP 후킹 실패 - 폴링으로만 동작합니다");
        }

        // text 프로퍼티 setter + 문자열 하나만 받는 SetText 오버로드
        private static IEnumerable<MethodBase> Targets()
        {
            var list = new List<MethodBase>();
            var setter = AccessTools.PropertySetter(typeof(TMP_Text), "text");
            if (setter != null) list.Add(setter);
            foreach (var m in typeof(TMP_Text).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "SetText") continue;
                var ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(string)) list.Add(m);
            }
            return list;
        }

        private static void TextPrefix(ref string __0)
        {
            if (string.IsNullOrEmpty(__0)) return;
            try
            {
                string t = Fixer.TranslateForHook(__0);
                if (t != null) __0 = t;
            }
            catch { }
        }
    }

    // XUnity.AutoTranslator의 번역 엔진을 그대로 빌려 쓴다.
    // 하드 참조를 두면 번역기가 없거나 로드 순서가 밀렸을 때 통째로 죽으므로 리플렉션으로 붙는다.
    internal static class XUnityBridge
    {
        private static object _translator;
        private static MethodInfo _tryTranslate;
        private static int _attempts;
        private static bool _warned;

        internal static bool Available
        {
            get { Resolve(); return _tryTranslate != null; }
        }

        // 번역기는 우리보다 늦게 로드될 수 있다(Mods 폴더 알파벳 순).
        // 연결될 때까지 다시 시도하되, 매 호출마다 어셈블리를 훑으면 비싸므로 띄엄띄엄 본다.
        private static void Resolve()
        {
            if (_tryTranslate != null) return;
            if (_attempts++ % 120 != 0) return;
            try
            {
                Type t = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != "XUnity.AutoTranslator.Plugin.Core") continue;
                    t = asm.GetType("XUnity.AutoTranslator.Plugin.Core.AutoTranslator");
                    break;
                }
                if (t == null) return;
                var def = t.GetProperty("Default", BindingFlags.Public | BindingFlags.Static);
                if (def == null) return;
                _translator = def.GetValue(null);
                if (_translator == null) return;
                _tryTranslate = _translator.GetType().GetMethod("TryTranslate",
                    new[] { typeof(string), typeof(string).MakeByRefType() });
                if (_tryTranslate != null)
                {
                    KLog.Info("XUnity 번역 엔진 연결됨");
                    // 연결 전에 "번역 없음"으로 기억해 둔 것들을 다시 판단하게 한다
                    Fixer.ClearHookCache();
                }
            }
            catch (Exception e)
            {
                if (!_warned) { _warned = true; KLog.Warn("XUnity 연결 실패(사전만 사용): " + e.Message); }
            }
        }

        // 정규식·조립 번역까지 처리하려면 번역기 쪽 캐시를 거쳐야 한다
        internal static string Translate(string src)
        {
            Resolve();
            if (_tryTranslate == null) return null;
            try
            {
                var args = new object[] { src, null };
                bool ok = (bool)_tryTranslate.Invoke(_translator, args);
                if (ok) return (string)args[1];
            }
            catch { }
            return null;
        }
    }
}
#endif
