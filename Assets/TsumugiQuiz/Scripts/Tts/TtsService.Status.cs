using System;
using UnityEngine;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// <see cref="TtsService"/> のうち、内部状態の書き換え（<see cref="SetState"/>）と
    /// <see cref="TtsService.StatusChanged"/> の発火（<see cref="NotifyStatusChanged"/>）をまとめた部分（#140）。
    /// ポーリング（<see cref="TtsService.Update"/>）自体は本体側に残す。
    /// </summary>
    public sealed partial class TtsService
    {
        /// <summary>
        /// <see cref="TtsService.StatusChanged"/> の購読者を 1 件ずつ呼び出す。ある購読者が例外を投げても、
        /// 他の購読者への通知やこのメソッド自体の呼び出し元（<see cref="TtsService.Update"/>）に
        /// 影響しないよう隔離する（#25 LOW）。
        /// </summary>
        private void NotifyStatusChanged()
        {
            var handler = StatusChanged;
            if (handler == null) return;

            var status = Status;
            foreach (var subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((Action<TtsServiceStatus>)subscriber)(status);
                }
                catch (Exception e)
                {
                    Debug.LogError("[TtsService] StatusChanged の購読者で例外が発生しました。他の購読者には影響しません。");
                    Debug.LogException(e);
                }
            }
        }

        private void SetState(
            TtsServiceState state, string reason,
            TtsUnavailableReason unavailableReason = TtsUnavailableReason.InitializationFailed,
            string detail = null)
        {
            _stateReason = reason;
            _stateUnavailableReason = unavailableReason;
            _stateDetail = detail;
            _state = state;
        }
    }
}
