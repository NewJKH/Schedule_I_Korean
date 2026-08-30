#if MELON
using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(KoreanTextFixer.MelonPlugin), "Korean Text Fixer", "1.8.0", "Schedule I 한글패치")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace KoreanTextFixer
{
    public class MelonPlugin : MelonMod
    {
        private Fixer _fixer;

        public override void OnInitializeMelon()
        {
            KLog.Info = s => LoggerInstance.Msg(s);
            KLog.Warn = s => LoggerInstance.Warning(s);
            KLog.Error = s => LoggerInstance.Error(s);
            try
            {
                // 프레임 부담의 출처를 가리려면 후킹과 폴링을 따로 끌 수 있어야 한다.
                // UserData\MelonPreferences.cfg 에서 바꾸고 게임을 다시 켜면 적용된다.
                var cfg = MelonPreferences.CreateCategory("KoreanTextFixer", "한글패치");
                var useHook = cfg.CreateEntry("TmpHook", true, "TMP 후킹", "끄면 폴링만으로 번역합니다");
                var usePolling = cfg.CreateEntry("Polling", true, "폴링 보정", "끄면 후킹만으로 번역합니다");

                // MelonMod판 XUnity.AutoTranslator는 번역 파일을 게임폴더\AutoTranslator 아래에 둔다
                Translations.Load(Path.Combine(MelonEnvironment.GameRootDirectory, "AutoTranslator", "Translation", "ko", "Text"));

                // XUnity가 MelonLoader에서 TMP를 후킹하지 못하므로 직접 건다 (자세한 이유는 TmpHook.Melon.cs)
                if (useHook.Value) TmpHook.Install(HarmonyInstance);
                else KLog.Info("설정에 따라 TMP 후킹을 끕니다");
                if (!usePolling.Value) KLog.Info("설정에 따라 폴링을 끕니다");

                _fixer = new Fixer(usePolling.Value);
                KLog.Info("KoreanTextFixer 1.8.0 (MelonLoader) loaded. entries=" + Translations.Dict.Count + " rules=" + Rules.Count);
            }
            catch (Exception e)
            {
                KLog.Error("KoreanTextFixer init failed: " + e);
            }
        }

        public override void OnUpdate()
        {
            if (_fixer != null) _fixer.Tick();
        }
    }
}
#endif
