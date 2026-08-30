#if MELON
using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(KoreanTextFixer.MelonPlugin), "Korean Text Fixer", "1.4.0", "Schedule I 한글패치")]
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
                // MelonMod판 XUnity.AutoTranslator는 번역 파일을 게임폴더\AutoTranslator 아래에 둔다
                Translations.Load(Path.Combine(MelonEnvironment.GameRootDirectory, "AutoTranslator", "Translation", "ko", "Text"));
                _fixer = new Fixer();
                KLog.Info("KoreanTextFixer 1.4.0 (MelonLoader) loaded. entries=" + Translations.Dict.Count);
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
