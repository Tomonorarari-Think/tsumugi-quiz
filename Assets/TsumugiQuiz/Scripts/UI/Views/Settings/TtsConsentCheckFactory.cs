using System;

namespace TsumugiQuiz.UI.Views.Settings
{
    /// <summary>
    /// 読み上げ（TTS）へ差し込む「利用規約に同意済みか」の判定（<c>consentCheck</c>）を作る共通ファクトリ
    /// （issue #127。<see cref="TtsSettingsProviderFactory"/> と対になる、もう一方の注入物）。
    /// issue #139 以降は<b>立ち絵（<see cref="TsumugiQuiz.UI.CharacterView"/>）の既定の同意判定も本ファクトリを使う</b>。
    /// FR-74 が TTS と立ち絵を同じ条件で扱う以上、判定の出所は 1 つに保つべきという理由で共用しており、
    /// クラス名の <c>Tts</c> は #127 の命名を引き継いでいるだけで用途を TTS に限定する意味ではない
    /// （改名は呼び出し側 6 か所に波及するため本 issue では行わない）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>TsumugiQuiz.Tts</c> 層から <c>UI</c> 層の <see cref="ConsentGate"/> は参照できない
    /// （asmdef の依存方向は UI → Tts の一方向、docs/architecture.md §3）ため、同意判定は
    /// <c>Func&lt;bool&gt;</c> として呼び出し側（UI 層）が注入する。<b>渡すのは判定結果ではなく判定関数</b>で、
    /// 合成・出題のたびに評価されるため、ゲーム進行中に同意を撤回（FR-75）しても次の読み上げから止まる。
    /// </para>
    /// <para>
    /// 呼び出し元は次の 6 か所で、いずれも本ファクトリを経由する（判定の出所を 1 つに保つため）:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     <see cref="TsumugiQuiz.UI.Views.Game.GameView"/>（<c>GameView.Tts.cs</c>、issue #127）が
    ///     セッション取得時に <c>TtsSyncPlayer.SetConsentCheck</c> へ渡す（ゲームプレイ中の読み上げ）
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="TsumugiQuiz.UI.Views.QuestionEditor.QuestionEditorView"/>
    ///     （<c>QuestionEditorView.Form.Tts.cs</c>、issue #32）が読み上げプレビューの
    ///     <c>TtsService.EnsureInitializedAsync</c> へ渡す
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="TtsStatusPanel"/> の「再試行」が <c>TtsService.RetryInitializeAsync</c> へ渡す
    ///     （Title / Settings / 問題エディタのいずれから開いても同じ。渡さないと再試行のたびに
    ///     <c>TtsService</c> 側の同意確認が「制限なし」へ戻ってしまう）
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="TsumugiQuiz.UI.Views.TitleView"/> / <see cref="SettingsView"/> は
    ///     <see cref="TtsStatusPanel"/> 経由（3 と同じ）
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="TtsAppSettingsReloader"/>（issue #138）が、アプリ設定の保存時に
    ///     (a) 再初期化してよいか（未同意・撤回後はロードし直さない）の判定と、
    ///     (b) <c>TtsService.RetryInitializeAsync</c> へ渡す <c>consentCheck</c> の両方に使う
    ///   </description></item>
    ///   <item><description>
    ///     立ち絵の表示可否: <see cref="TsumugiQuiz.UI.CharacterView"/>（issue #139）。
    ///     構築時・出題ごと（<c>GameSession.QuestionShown</c>）・パネル接続時に評価する
    ///   </description></item>
    /// </list>
    /// <para>
    /// 判定そのもの（<see cref="ConsentGate.HasUserConsented"/>）は例外時に安全側（未同意）へ倒す。
    /// 受け取る側（<c>TtsService.HasConsent</c> / <c>TtsSyncPlayer.HasUserConsented</c>）でも
    /// 例外は未同意として扱うため、二重に安全側へ倒れる。
    /// </para>
    /// </remarks>
    internal static class TtsConsentCheckFactory
    {
        /// <summary>
        /// 合成・出題のたびに評価される同意判定を返す（<see cref="ConsentGate.HasUserConsented"/>）。
        /// 返り値は常に非 null（null を渡すと受け取り側は「制限しない」と解釈してしまうため）。
        /// </summary>
        public static Func<bool> Build() => ConsentGate.HasUserConsented;
    }
}
