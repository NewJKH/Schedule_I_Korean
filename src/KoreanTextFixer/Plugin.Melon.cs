#if MELON
using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(KoreanTextFixer.MelonPlugin), "Korean Text Fixer", "1.14.1", "Schedule I 한글패치")]
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
                var logMissing = cfg.CreateEntry("LogMissing", true, "미번역 수집",
                    "화면에 나왔지만 번역이 없는 문구를 UserData\\KoreanTextFixer_missing.txt 에 모읍니다");

                // MelonMod판 XUnity.AutoTranslator는 번역 파일을 게임폴더\AutoTranslator 아래에 둔다
                Translations.Load(Path.Combine(MelonEnvironment.GameRootDirectory, "AutoTranslator", "Translation", "ko", "Text"));

                if (logMissing.Value)
                {
                    MissingLog.Enable(Path.Combine(MelonEnvironment.UserDataDirectory, "KoreanTextFixer_missing.txt"));
                }

                // XUnity가 MelonLoader에서 TMP를 후킹하지 못하므로 직접 건다 (자세한 이유는 TmpHook.Melon.cs)
                if (useHook.Value) TmpHook.Install(HarmonyInstance);
                else KLog.Info("설정에 따라 TMP 후킹을 끕니다");
                if (!usePolling.Value) KLog.Info("설정에 따라 폴링을 끕니다");

                Diagnostics.Init(Path.Combine(MelonEnvironment.UserDataDirectory, "KoreanTextFixer_screen.txt"));
                _fixer = new Fixer(usePolling.Value);
                KLog.Info("KoreanTextFixer 1.14.1 (MelonLoader) loaded. entries=" + Translations.Dict.Count + " rules=" + Rules.Count);
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

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            // 파괴 중인 오브젝트를 건드리지 않도록 폴링 목록을 버린다
            if (_fixer != null) _fixer.OnSceneChange();
        }

        public override void OnApplicationQuit()
        {
            if (_fixer != null) _fixer.OnSceneChange();
            MissingLog.Flush();
        }
    }
}
#endif
