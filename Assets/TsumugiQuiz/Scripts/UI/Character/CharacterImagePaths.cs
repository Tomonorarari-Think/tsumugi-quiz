using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// 立ち絵画像の配置パス解決（issue #24 / 表情差分は #86、External/README.md §5 /
    /// scripts/generate-tsumugi-expressions.ps1）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 立ち絵原本は二次配布禁止（docs/licenses.md §3）のためビルドに同梱せず、
    /// <see cref="AppPaths.DataRoot"/>（<c>Application.persistentDataPath</c> 相当、#71）配下に
    /// ユーザー自身が配置したものを実行時に読む。<c>consent.json</c>（<c>JsonConsentStorage</c>）・
    /// TTS キャッシュ（<c>TtsService</c>）と同じ置き場所の考え方（<see cref="AppPaths.Combine"/>）。
    /// </para>
    /// <para>
    /// 表情差分（#86）も同じ扱いで、原本の PSD から <c>scripts/generate-tsumugi-expressions.ps1</c> が
    /// **ユーザー自身の環境で**生成し、同じディレクトリ（<see cref="DirectoryName"/>）に置く。
    /// 加工物（差分 PNG）もリポジトリ・配布物には含めない。
    /// </para>
    /// <para>
    /// フォールバック規則（<see cref="GetFileNameCandidates"/>）: 状態専用の差分 → 待機の差分 →
    /// 従来の全身 PNG（<see cref="FileName"/>、#24 で <c>setup-external.ps1</c> が配置するもの）。
    /// すべて無ければ <see cref="CharacterView"/> は立ち絵を表示しない。
    /// </para>
    /// <para>
    /// #212 で増やした表情のうち、誤答の瞬間・時間切れ・回答できる人がいないは、待機の前に
    /// 意味の近い既存の表情（時間切れ → 不正解）を挟む。#86 の 4 枚しか生成していない利用者でも、
    /// これらの場面では不正解の顔が出る。
    /// </para>
    /// </remarks>
    public static class CharacterImagePaths
    {
        /// <summary>データルート直下のディレクトリ名。</summary>
        public const string DirectoryName = "tsumugi";

        /// <summary>
        /// 表情差分が無い場合に使う全身 PNG のファイル名（issue #24、<c>setup-external.ps1</c> が配置）。
        /// </summary>
        public const string FileName = "tsumugi_v2.png";

        /// <summary>待機の表情差分（#86）。</summary>
        public const string IdleFileName = "tsumugi_idle.png";

        /// <summary>読み上げ中の表情差分（#86）。</summary>
        public const string ReadingFileName = "tsumugi_reading.png";

        /// <summary>正解の表情差分（#86）。</summary>
        public const string CorrectFileName = "tsumugi_correct.png";

        /// <summary>不正解の確定の表情差分（#86）。</summary>
        public const string WrongFileName = "tsumugi_wrong.png";

        /// <summary>自分が回答権を得たときの表情差分（#212）。</summary>
        public const string BuzzSelfFileName = "tsumugi_buzz_self.png";

        /// <summary>他の参加者が回答権を得たときの表情差分（#212）。</summary>
        public const string BuzzOtherFileName = "tsumugi_buzz_other.png";

        /// <summary>誤答の瞬間（受付の開き直し）の表情差分（#212）。</summary>
        public const string WrongMomentFileName = "tsumugi_wrong_moment.png";

        /// <summary>時間切れの表情差分（#212）。</summary>
        public const string TimedOutFileName = "tsumugi_timeout.png";

        /// <summary>回答できる人がいないときの表情差分（#212）。</summary>
        public const string NoEligibleBuzzersFileName = "tsumugi_no_eligible.png";

        // 不変（配列を直接返さず IReadOnlyList として公開する）。状態専用 → 待機 → 従来の全身 PNG の順。
        private static readonly IReadOnlyList<string> IdleCandidates =
            Array.AsReadOnly(new[] { IdleFileName, FileName });

        private static readonly IReadOnlyList<string> ReadingCandidates =
            Array.AsReadOnly(new[] { ReadingFileName, IdleFileName, FileName });

        private static readonly IReadOnlyList<string> CorrectCandidates =
            Array.AsReadOnly(new[] { CorrectFileName, IdleFileName, FileName });

        private static readonly IReadOnlyList<string> WrongCandidates =
            Array.AsReadOnly(new[] { WrongFileName, IdleFileName, FileName });

        private static readonly IReadOnlyList<string> BuzzSelfCandidates =
            Array.AsReadOnly(new[] { BuzzSelfFileName, IdleFileName, FileName });

        private static readonly IReadOnlyList<string> BuzzOtherCandidates =
            Array.AsReadOnly(new[] { BuzzOtherFileName, IdleFileName, FileName });

        private static readonly IReadOnlyList<string> WrongMomentCandidates =
            Array.AsReadOnly(new[] { WrongMomentFileName, WrongFileName, IdleFileName, FileName });

        private static readonly IReadOnlyList<string> TimedOutCandidates =
            Array.AsReadOnly(new[] { TimedOutFileName, WrongFileName, IdleFileName, FileName });

        private static readonly IReadOnlyList<string> NoEligibleBuzzersCandidates =
            Array.AsReadOnly(new[] { NoEligibleBuzzersFileName, TimedOutFileName, WrongFileName, IdleFileName, FileName });

        /// <summary>
        /// 立ち絵画像（従来の全身 PNG）の絶対パスを解決する（<see cref="AppPaths.DataRoot"/> 配下）。
        /// </summary>
        /// <exception cref="InvalidOperationException"><see cref="AppPaths.DataRoot"/> が未設定のとき。</exception>
        public static string ResolveImagePath() => AppPaths.Combine(DirectoryName, FileName);

        /// <summary>
        /// 指定した状態で最優先に使うファイル名（表情差分）を返す。
        /// </summary>
        /// <param name="state">立ち絵の表示状態。</param>
        /// <exception cref="ArgumentOutOfRangeException">未知の状態のとき。</exception>
        public static string GetFileName(CharacterState state) => GetFileNameCandidates(state)[0];

        /// <summary>
        /// 指定した状態で読み込みを試みるファイル名を、優先順に返す
        /// （状態専用の差分 →（#212 の一部の表情は意味の近い既存の差分 →）待機の差分 → 従来の全身 PNG）。
        /// 先頭（添字 0）がその状態専用のファイル。
        /// </summary>
        /// <param name="state">立ち絵の表示状態。</param>
        /// <exception cref="ArgumentOutOfRangeException">未知の状態のとき。</exception>
        public static IReadOnlyList<string> GetFileNameCandidates(CharacterState state)
        {
            switch (state)
            {
                case CharacterState.Idle:
                    return IdleCandidates;
                case CharacterState.Reading:
                    return ReadingCandidates;
                case CharacterState.Correct:
                    return CorrectCandidates;
                case CharacterState.Wrong:
                    return WrongCandidates;
                case CharacterState.BuzzSelf:
                    return BuzzSelfCandidates;
                case CharacterState.BuzzOther:
                    return BuzzOtherCandidates;
                case CharacterState.WrongMoment:
                    return WrongMomentCandidates;
                case CharacterState.TimedOut:
                    return TimedOutCandidates;
                case CharacterState.NoEligibleBuzzers:
                    return NoEligibleBuzzersCandidates;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(state), state, "未知の立ち絵状態です。");
            }
        }

        /// <summary>
        /// 指定した状態で読み込みを試みる絶対パスを、優先順に返す（<see cref="AppPaths.DataRoot"/> 配下）。
        /// </summary>
        /// <param name="state">立ち絵の表示状態。</param>
        /// <exception cref="ArgumentOutOfRangeException">未知の状態のとき。</exception>
        /// <exception cref="InvalidOperationException"><see cref="AppPaths.DataRoot"/> が未設定のとき。</exception>
        public static IReadOnlyList<string> ResolveImagePathCandidates(CharacterState state)
        {
            var fileNames = GetFileNameCandidates(state);
            var paths = new string[fileNames.Count];
            for (var i = 0; i < fileNames.Count; i++)
            {
                paths[i] = AppPaths.Combine(DirectoryName, fileNames[i]);
            }

            return Array.AsReadOnly(paths);
        }
    }
}
