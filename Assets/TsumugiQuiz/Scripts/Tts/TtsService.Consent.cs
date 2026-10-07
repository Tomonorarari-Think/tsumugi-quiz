using System;
using UnityEngine;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// <see cref="TtsService"/> のうち、利用規約への同意ゲートの判定（#37 / #127、
    /// requirements.md FR-74 / FR-75）をまとめた部分（#140）。
    /// 判定関数（<c>_consentCheck</c>）自体の登録は <c>TtsService.Initialization.cs</c>
    /// （<see cref="TtsService.ConfigureDefaults"/> / <see cref="TtsService.Initialize"/>）が担う。
    /// </summary>
    public sealed partial class TtsService
    {
        /// <summary>
        /// 同意ゲート（#37、FR-74 / FR-75）。判定関数が渡されていなければ制限しない。
        /// 判定自体が失敗したときは<b>安全側（未同意扱い）</b>に倒す。
        /// </summary>
        private bool HasConsent()
        {
            if (EvaluateConsent()) return true;

            Debug.LogWarning("[TtsService] 利用規約に同意していないため読み上げを行いません（FR-74）。");
            return false;
        }

        /// <summary>
        /// 同意ゲートを評価するだけの版（ログを出さない。<see cref="TtsService.IsConsentSatisfied"/> の実体）。
        /// 判定関数が未設定なら true（制限なし）、判定が例外になったら false（安全側）。
        /// </summary>
        private bool EvaluateConsent()
        {
            var check = _consentCheck;
            if (check == null) return true;

            try
            {
                return check();
            }
            catch (Exception e)
            {
                Debug.LogError($"[TtsService] 同意状況の判定に失敗しました。読み上げを行いません: {e.Message}");
                return false;
            }
        }
    }
}
