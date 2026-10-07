using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// <see cref="TtsUnavailableReason"/> 1件分の、ユーザー向け文言と対処案内。生成後は不変。
    /// いずれも UI にそのまま表示してよい文言である（ResultCode の数値やネイティブの原文、例外の型名は含まない）。
    /// </summary>
    public readonly struct TtsStatusMessage
    {
        public TtsStatusMessage(string headline, string guidance, string detail = null)
        {
            Headline = headline;
            Guidance = guidance;
            Detail = detail;
        }

        /// <summary>状態を短く表す見出し（例:「音声合成ライブラリが見つかりません。」）。</summary>
        public string Headline { get; }

        /// <summary>対処案内（配置手順の確認・同意手続きなど、次に何をすればよいか）。</summary>
        public string Guidance { get; }

        /// <summary>
        /// 任意の補足情報。現状は <see cref="TtsUnavailableReason.OnnxRuntimeVersionMismatch"/> の
        /// 対応バージョン範囲（「対応バージョンは 1.{min} 以上 1.{max} 以下です。」）だけがここに入る。
        /// バージョン番号のみを含む数値で、ResultCode やネイティブの原文は含めない（docs/tts.md §9.1）。
        /// 該当なしのときは null。
        /// </summary>
        public string Detail { get; }
    }

    /// <summary>
    /// <see cref="TtsUnavailableReason"/> ごとのユーザー向け文言・対処案内をまとめた純 C# クラス
    /// （docs/tts.md §9.1）。Unity API に依存しないため EditMode テストで全件検証できる。
    ///
    /// 配置手順の詳細はファイルパスや URL を直接案内せず、<c>TsumugiQuiz.UI.TtsStatusPanel</c> の
    /// 「配置手順を表示」ボタン（アプリ内蔵テキスト、docs/tts.md §9.1・#25 H-2）への誘導にとどめる。
    /// 同意手続きは利用規約画面（Terms View）を案内する。
    /// <b>ResultCode の数値・<c>[数値 名前]</c> 形式・例外の型名はここに含めない</b>
    /// （詳細はログにのみ残す。docs/tts.md §9 の共通原則）。バージョン番号（<see cref="TtsStatusMessage.Detail"/>）
    /// だけは docs/tts.md §9 の「min/max を示す」方針に従い例外的に数値を許容する。
    /// </summary>
    public static class TtsStatusMessages
    {
        private const string SetupGuidance = "「配置手順を表示」から手順を確認し、配置したら再試行してください。";

        private static readonly IReadOnlyDictionary<TtsUnavailableReason, TtsStatusMessage> Messages =
            new Dictionary<TtsUnavailableReason, TtsStatusMessage>
            {
                [TtsUnavailableReason.MissingCoreDll] = new TtsStatusMessage(
                    "音声合成ライブラリが見つかりません。",
                    SetupGuidance),

                [TtsUnavailableReason.MissingOnnxRuntime] = new TtsStatusMessage(
                    "音声合成に必要な ONNX Runtime が見つかりません。",
                    SetupGuidance),

                [TtsUnavailableReason.MissingDictionary] = new TtsStatusMessage(
                    "読み上げ用の辞書が見つかりません。",
                    SetupGuidance),

                [TtsUnavailableReason.MissingModel] = new TtsStatusMessage(
                    "音声モデルが見つかりません。",
                    SetupGuidance),

                [TtsUnavailableReason.OnnxRuntimeVersionMismatch] = new TtsStatusMessage(
                    "ONNX Runtime のバージョンが対応していない可能性があります。",
                    SetupGuidance),

                [TtsUnavailableReason.InitializationFailed] = new TtsStatusMessage(
                    "読み上げを初期化できませんでした。",
                    "時間をおいて再試行してください。改善しない場合は「配置手順を表示」の内容を見直すか、詳細をログで確認してください。"),

                [TtsUnavailableReason.ConsentNotGiven] = new TtsStatusMessage(
                    "利用規約への同意が必要です。",
                    "利用規約画面で内容を確認し、同意してください。"),

                [TtsUnavailableReason.UserSuppressed] = new TtsStatusMessage(
                    "読み上げが無効になっています。",
                    "設定で読み上げを有効にすると使えるようになります（このセッション限りの状態です）。"),
            };

        /// <summary>
        /// 指定した理由のユーザー向け文言・対処案内を返す。
        /// </summary>
        /// <param name="reason">理由。</param>
        /// <param name="detail">
        /// <see cref="TtsStatusMessage.Detail"/> に入れる任意の補足（現状は ONNX Runtime の対応バージョン範囲のみ）。
        /// 省略時（既定 null）は <see cref="TtsStatusMessage.Detail"/> も null になる。
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">未定義の <see cref="TtsUnavailableReason"/> のとき</exception>
        public static TtsStatusMessage For(TtsUnavailableReason reason, string detail = null)
        {
            if (!Messages.TryGetValue(reason, out var message))
            {
                throw new ArgumentOutOfRangeException(nameof(reason), reason, "未定義の TtsUnavailableReason です。");
            }

            return new TtsStatusMessage(message.Headline, message.Guidance, detail);
        }
    }
}
