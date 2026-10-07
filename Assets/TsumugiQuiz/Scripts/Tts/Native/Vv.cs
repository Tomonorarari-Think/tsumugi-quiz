using System;

namespace TsumugiQuiz.Tts.Native
{
    /// <summary>
    /// 結果コードを例外へ変換するラッパ（docs/tts-native-api.md §2.2）。
    /// 結果コードを握りつぶさず、必ずネイティブ側のメッセージを添えて投げる。
    /// </summary>
    internal static class Vv
    {
        /// <summary>Ok 以外なら <see cref="VoicevoxException"/> を投げる。</summary>
        public static void Check(VoicevoxResultCode code, string what)
        {
            if (code == VoicevoxResultCode.Ok) return;

            var nativeMessage = DescribeResultCode(code);
            throw new VoicevoxException(
                code,
                nativeMessage,
                $"{what} に失敗しました: [{(int)code} {code}] {nativeMessage}");
        }

        /// <summary>
        /// 結果コードに対応するネイティブ側メッセージを取得する。
        /// DLL が未配置の環境（External 未配置の EditMode テストなど）では列挙子名にフォールバックする。
        /// </summary>
        public static string DescribeResultCode(VoicevoxResultCode code)
        {
            try
            {
                return Utf8.FromPtr(VoicevoxNative.voicevox_error_result_to_message(code));
            }
            catch (DllNotFoundException)
            {
                return code.ToString();
            }
            catch (EntryPointNotFoundException)
            {
                return code.ToString();
            }
        }
    }
}
