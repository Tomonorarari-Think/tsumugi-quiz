namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// プレイヤー名の検証と正規化（docs/network.md §9）。
    /// クライアント側の入力チェックは UX のためだけで、サーバーは受信時に必ずこれを通す。
    /// </summary>
    public static class PlayerNameValidator
    {
        /// <summary>
        /// 名前の規則の要約（ユーザー向けの文言に埋め込む。issue #209）。
        /// 例: 「プレイヤー名を入力してください（{RuleSummary}）。」
        /// 改行は制御文字に含まれる。<see cref="ConnectionRejectionMessages.InvalidPlayerName"/> に埋め込むので、
        /// 自前の切断理由の上限（<see cref="DisconnectReasonSanitizer.MaxLength"/> の半分、50 文字）に収まる長さにしている。
        /// 区切りは「、」にする。Join 画面は文節に近い区切り（<c>PhraseSegmenter</c>）でだけ改行し、「・」では区切らないため、
        /// 「・」でつなぐと 1 つの片が表示欄の 1 行より長くなり、語の途中で折り返す（<c>DynamicTextWrappingSceneTests</c>）。
        /// </summary>
        public const string RuleSummary = "1〜16文字。制御文字、見えない文字、重ねすぎた記号は使えません";

        /// <summary>
        /// プレイヤー名を検証し、前後の空白を除いた正規化済みの名前を返す。
        /// 見えない文字と、基底文字 1 つあたり <see cref="TextRules.MaxCombiningMarksPerBase"/> 個を超える結合記号を含む名前は
        /// 拒否する（<see cref="TextRules.ContainsHiddenCharacters"/>。表示の整形と同じ規則。issue #209）。
        /// </summary>
        /// <param name="rawName">検証対象。null 可。</param>
        /// <param name="normalizedName">正規化済みの名前。失敗時は空文字。</param>
        /// <returns>規則を満たすなら true。</returns>
        public static bool TryNormalize(string rawName, out string normalizedName)
        {
            normalizedName = string.Empty;
            if (string.IsNullOrEmpty(rawName))
            {
                return false;
            }

            var trimmed = rawName.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            if (!TryCountCodePoints(trimmed, out var codePointCount))
            {
                // サロゲート単独（不正な UTF-16）を含む。
                return false;
            }

            if (codePointCount < ProtocolConstants.MinPlayerNameLength ||
                codePointCount > ProtocolConstants.MaxPlayerNameLength)
            {
                return false;
            }

            if (TextRules.ContainsControlCharacter(trimmed))
            {
                return false;
            }

            if (TextRules.ContainsHiddenCharacters(trimmed))
            {
                // 見えない文字で、ほかの名前と同じ見た目の別の名前を作れないようにする（#209）。
                return false;
            }

            normalizedName = trimmed;
            return true;
        }

        /// <summary>
        /// コードポイント数を数える。サロゲートペアは 1 文字、単独サロゲートは不正として false を返す。
        /// </summary>
        private static bool TryCountCodePoints(string value, out int count)
        {
            count = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    {
                        count = 0;
                        return false;
                    }

                    i++;
                }
                else if (char.IsLowSurrogate(c))
                {
                    count = 0;
                    return false;
                }

                count++;
            }

            return true;
        }
    }
}
