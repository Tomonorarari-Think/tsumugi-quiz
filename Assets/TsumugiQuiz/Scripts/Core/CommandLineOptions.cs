using System;
using System.Collections.Generic;
using System.Globalization;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// コマンドライン引数の解析（純 C#）。
    /// Unity 標準の引数（<c>-screen-width</c> など）と混在するため、自前でパースする
    /// （docs/network.md §10.3）。キーは大文字小文字を区別しない。
    ///
    /// 形式:
    /// <code>
    /// -tq-port 7777      → キー "-tq-port" に値 "7777"
    /// -tq-port=7777      → 同じ（"=" 区切りも受理する）
    /// -tq-host           → キー "-tq-host" に空文字（フラグ）
    /// </code>
    /// 値を伴わないキーの直後に別のキー（<c>-</c> 始まり）が来た場合はフラグとして扱う。
    /// 同じキーが複数回現れた場合は最後の指定を採用する。
    /// </summary>
    public sealed class CommandLineOptions
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        private CommandLineOptions(IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        /// <summary>引数が 1 つも無い空の解析結果。</summary>
        public static CommandLineOptions Empty { get; } =
            new CommandLineOptions(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        /// <summary>解析済みのキー数。</summary>
        public int Count => _values.Count;

        /// <summary>
        /// 引数配列を解析する。実行ファイルパス（先頭要素）のようにハイフンで始まらない要素は無視する。
        /// </summary>
        /// <param name="args">解析対象。null なら <see cref="Empty"/> と同等。</param>
        public static CommandLineOptions Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (args == null)
            {
                return new CommandLineOptions(values);
            }

            for (var i = 0; i < args.Length; i++)
            {
                var token = args[i];
                if (string.IsNullOrEmpty(token) || token[0] != '-' || token.Length < 2)
                {
                    continue;
                }

                // "-tq-port=7777" 形式。最初の '=' より前をキー、後ろを値とする。
                var separatorIndex = token.IndexOf('=');
                if (separatorIndex > 1)
                {
                    var inlineKey = token.Substring(0, separatorIndex);
                    values[inlineKey] = token.Substring(separatorIndex + 1);
                    continue;
                }

                var hasValue = i + 1 < args.Length
                               && !string.IsNullOrEmpty(args[i + 1])
                               && args[i + 1][0] != '-';

                values[token] = hasValue ? args[i + 1] : string.Empty;
                if (hasValue)
                {
                    i++;
                }
            }

            return new CommandLineOptions(values);
        }

        /// <summary>指定したキーが存在するか（値の有無は問わない）。</summary>
        public bool Contains(string key) => key != null && _values.ContainsKey(key);

        /// <summary>
        /// 文字列の値を取得する。複数のキー名を渡すと、先に見つかった非空の値を返す（別名対応）。
        /// </summary>
        public bool TryGetString(out string value, params string[] keys)
        {
            value = null;
            if (keys == null)
            {
                return false;
            }

            foreach (var key in keys)
            {
                if (key != null && _values.TryGetValue(key, out var found) && !string.IsNullOrEmpty(found))
                {
                    value = found;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// ポート番号などの <see cref="ushort"/> 値を取得する。数値として読めない場合は false。
        /// </summary>
        public bool TryGetUInt16(out ushort value, params string[] keys)
        {
            value = 0;
            if (!TryGetString(out var raw, keys))
            {
                return false;
            }

            return ushort.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// <c>-tq-window</c>（"x,y,w,h"）などのウィンドウ位置・サイズ値を取得する
        /// （<see cref="LaunchWindowRect"/>、issue #8）。解析できない場合は false。
        /// </summary>
        public bool TryGetWindowRect(out LaunchWindowRect rect, params string[] keys)
        {
            rect = default;
            return TryGetString(out var raw, keys) && LaunchWindowRect.TryParse(raw, out rect);
        }
    }
}
