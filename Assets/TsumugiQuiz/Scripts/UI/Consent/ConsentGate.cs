using System;
using TsumugiQuiz.Core;
using UnityEngine;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// 「利用規約に同意済みか」の判定と、既定の <see cref="ConsentStore"/> 生成をまとめる小さなヘルパー。
    /// <see cref="Views.DefaultViewControllerRegistrations.ConfigureInitialView"/>（起動時の Terms/Title 振り分け、
    /// requirements.md FR-71・FR-74）と <see cref="Views.TermsView"/> の両方から使う。
    ///
    /// TTS（音声合成）・立ち絵表示の機能は、実行する直前に必ず <see cref="HasUserConsented"/> を確認し、
    /// false の場合は機能を実行せず Terms 画面へ誘導すること（FR-74: 同意記録が無い状態では
    /// TTS・立ち絵を利用する機能へ進めない）。この起動時の Terms/Title 振り分けだけでは、
    /// 撤回（FR-75）後に既存の画面へ留まったまま TTS/立ち絵の呼び出しコードパスが実行されるのを
    /// 防げないため、呼び出し側での確認が必須。
    ///
    /// 現在の確認箇所（#139 時点。判定関数は <see cref="Views.Settings.TtsConsentCheckFactory"/> に集約）:
    /// <list type="bullet">
    ///   <item><description>
    ///     ゲームプレイ中の読み上げ: アプリ起動時に
    ///     <see cref="Views.DefaultViewControllerRegistrations.ConfigureTtsConsentGate"/> が
    ///     <c>TtsService</c> へ登録し、<c>GameView.WireTtsSyncPlayer</c> が <c>TtsSyncPlayer</c> にも差し込む。
    ///     <b>出題ごと・再生開始ごとに評価される</b>ので、進行中の撤回は次の問題から効く（#127）
    ///   </description></item>
    ///   <item><description>問題エディタの読み上げプレビュー: <c>QuestionEditorView.Form.Tts</c>（#32）。試聴のたびに評価</description></item>
    ///   <item><description>読み上げの再試行: <see cref="TtsStatusPanel"/>（#127）。状態表示の理由分岐は <c>ResolveReason</c></description></item>
    ///   <item><description>
    ///     立ち絵: <c>CharacterView</c>（#24 / #86）。<b>構築時（＝ Game View を表示するたび）と
    ///     出題ごと（<c>GameSession.QuestionShown</c>）・パネルへの接続時に評価される</b>ので、
    ///     読み上げと同じく進行中の撤回は次の問題から効く（#139）。
    ///     表示中のフレームで即座に消えるわけではない（毎フレーム <c>consent.json</c> を
    ///     読み直さないため）。明示的に反映したい経路のために <c>RefreshVisibility()</c> も公開している
    ///   </description></item>
    /// </list>
    /// </summary>
    public static class ConsentGate
    {
        private static readonly Func<IConsentStorage> DefaultStorageFactory = () => new JsonConsentStorage();
        private static Func<IConsentStorage> _storageFactory = DefaultStorageFactory;

        /// <summary>本番用の既定の ConsentStore（既定では JsonConsentStorage を使う）を生成する。</summary>
        public static ConsentStore CreateDefaultStore()
        {
            return new ConsentStore(_storageFactory());
        }

        /// <summary>
        /// 同梱された全規約に、現在のテキストのハッシュで同意済みかどうかを判定する。
        /// 1件でも未同意・ハッシュ不一致（規約更新による再同意要求）があれば false。
        /// リソース読み込み・ファイル I/O 等で例外が発生した場合も、安全側（未同意扱い＝Terms 表示）に倒し、
        /// 詳細はログに残す（起動不能にしないため。ここで例外を伝播させると Main シーンの初期化自体が失敗する）。
        /// </summary>
        public static bool HasUserConsented()
        {
            try
            {
                var store = CreateDefaultStore();
                var requiredTerms = TermsCatalog.LoadRequiredTerms();
                return store.HasAcceptedAll(requiredTerms);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConsentGate] 同意状況の判定に失敗しました。安全側として未同意扱いにします: {ex}");
                return false;
            }
        }

        /// <summary>
        /// テストから永続化の実装を差し替えるためのフック。通常のコードパスから呼ぶ必要はない。
        /// null を渡すと既定（JsonConsentStorage）に戻る。
        /// </summary>
        public static void SetStorageFactoryForTesting(Func<IConsentStorage> factory)
        {
            _storageFactory = factory ?? DefaultStorageFactory;
        }
    }
}
