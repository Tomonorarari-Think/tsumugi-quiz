using System;
using System.Globalization;
using System.Text;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// ホストの IPv4 アドレスとポートを、口頭でも伝えられる 12 文字の参加コードへ可逆変換する。
    /// 仕様は docs/network-joincode.md §1（12 文字方式: 50bit ペイロード + 10bit チェック）に従う。
    /// Unity API には依存しない純 C#。
    /// </summary>
    /// <remarks>
    /// ビット構成:
    /// <code>
    /// V = (version &lt;&lt; 48) | (ipv4 &lt;&lt; 16) | port   // 50 bit
    /// check = V mod 1021                            // 10 bit (0..1020)
    /// W = (V &lt;&lt; 10) | check                       // 60 bit = Base32 で 12 文字
    /// </code>
    /// </remarks>
    public static class JoinCodeCodec
    {
        /// <summary>Crockford Base32 の符号化アルファベット（I / L / O / U を除く 32 記号。§1.3）。</summary>
        public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        /// <summary>チェック値の法。1024 未満で最大の素数（§1.2）。</summary>
        public const int CheckMod = 1021;

        /// <summary>正規化後の参加コードの文字数。</summary>
        public const int CodeLength = 12;

        /// <summary>表示形式のグループ区切り文字。</summary>
        public const char GroupSeparator = '-';

        /// <summary>
        /// 表示形式 1 グループあたりの文字数（XXXX-XXXX-XXXX）。
        /// <see cref="TsumugiQuiz.Core.JoinCodeInputFormatter"/> など、参加コードのハイフン整形を
        /// 行う他のクラスもこの定数を参照する（グルーピング幅の定義を 1 箇所に保つため）。
        /// </summary>
        public const int GroupLength = 4;

        /// <summary>1 文字が表すビット数（Base32）。</summary>
        private const int BitsPerChar = 5;

        /// <summary>チェック値のビット数。</summary>
        private const int CheckBits = 10;

        /// <summary>チェック値を取り出すマスク（下位 10 bit）。</summary>
        private const ulong CheckMask = (1UL << CheckBits) - 1UL;

        /// <summary>version が占めるビット数（2 bit）。</summary>
        private const int VersionBits = 2;

        /// <summary>version の最大値（2 bit のため 3）。</summary>
        private const int MaxVersion = (1 << VersionBits) - 1;

        /// <summary>V における version のシフト量。</summary>
        private const int VersionShift = 48;

        /// <summary>V における IPv4 のシフト量。</summary>
        private const int Ipv4Shift = 16;

        /// <summary>ポート番号の最大値。</summary>
        private const int MaxPort = 65535;

        /// <summary>IPv4 のオクテット数。</summary>
        private const int OctetCount = 4;

        /// <summary>
        /// IPv4 アドレスとポートを表示形式（<c>XXXX-XXXX-XXXX</c>）の参加コードへ変換する。
        /// </summary>
        /// <param name="ip">ドット区切りの IPv4 アドレス（例 <c>203.0.113.5</c>）。</param>
        /// <param name="port">ポート番号（0〜65535）。</param>
        /// <param name="version">コードのバージョン（0〜3）。0 は IPv4 + ポートの直接接続。</param>
        /// <exception cref="JoinCodeException">引数が仕様の範囲外のとき。</exception>
        public static string Encode(string ip, int port, int version = 0)
        {
            var ipv4 = ParseIpv4(ip);

            if (port < 0 || port > MaxPort)
            {
                throw new JoinCodeException(
                    JoinCodeError.InvalidPort,
                    "ポート番号が範囲外です（0〜65535 を指定してください）: " + port.ToString(CultureInfo.InvariantCulture));
            }

            if (version < 0 || version > MaxVersion)
            {
                throw new JoinCodeException(
                    JoinCodeError.InvalidVersion,
                    "参加コードのバージョンが範囲外です（0〜3 を指定してください）: " + version.ToString(CultureInfo.InvariantCulture));
            }

            // int を直接 ulong にキャストすると符号拡張の警告（CS0675）になるため、必ず uint を経由する。
            var v = ((ulong)(uint)version << VersionShift) | ((ulong)ipv4 << Ipv4Shift) | (ulong)(uint)port;
            var check = v % CheckMod;
            var w = (v << CheckBits) | check;

            return Format(ToBody(w));
        }

        /// <summary>
        /// 参加コードを IPv4 アドレスとポートへ戻す。入力は <see cref="Normalize"/> で正規化してから解釈する。
        /// </summary>
        /// <param name="code">参加コード（表示形式・小文字・全角・O/I/L の誤入力を許容する）。</param>
        /// <returns>バージョン、IPv4 アドレス、ポート番号。</returns>
        /// <exception cref="JoinCodeException">
        /// 長さ違い・使えない文字・チェック値が 1021 以上・チェック不一致のいずれかのとき。
        /// </exception>
        public static (int Version, string Ip, int Port) Decode(string code)
        {
            if (!TryDecodeInternal(code, out var result, out var error, out var message))
            {
                throw new JoinCodeException(error, message);
            }

            return result;
        }

        /// <summary>
        /// 例外を投げない <see cref="Decode"/>。UI の入力検証のように、失敗が通常系である場面で使う。
        /// </summary>
        /// <param name="code">参加コード。</param>
        /// <param name="result">成功したときのバージョン・IPv4 アドレス・ポート番号。失敗時は既定値。</param>
        /// <param name="error">失敗理由。成功時は <see cref="JoinCodeError.None"/>。</param>
        /// <returns>デコードに成功したかどうか。</returns>
        public static bool TryDecode(string code, out (int Version, string Ip, int Port) result, out JoinCodeError error)
        {
            return TryDecodeInternal(code, out result, out error, out _);
        }

        /// <summary>
        /// <see cref="TryDecode(string, out (int, string, int), out JoinCodeError)"/> に、
        /// そのまま UI へ表示できる日本語メッセージも合わせて返すオーバーロード（M-2）。
        /// メッセージの文言は <see cref="JoinCodeErrorMessages"/> を単一の情報源としており、
        /// 本メソッドと <see cref="Decode"/> の例外メッセージはどちらも同じ対応表から作られる。
        /// </summary>
        /// <param name="code">参加コード。</param>
        /// <param name="result">成功したときのバージョン・IPv4 アドレス・ポート番号。失敗時は既定値。</param>
        /// <param name="error">失敗理由。成功時は <see cref="JoinCodeError.None"/>。</param>
        /// <param name="message">失敗時にそのまま表示できる日本語メッセージ。成功時は空文字。</param>
        /// <returns>デコードに成功したかどうか。</returns>
        public static bool TryDecode(
            string code,
            out (int Version, string Ip, int Port) result,
            out JoinCodeError error,
            out string message)
        {
            var succeeded = TryDecodeInternal(code, out result, out error, out message);
            message ??= string.Empty;
            return succeeded;
        }

        /// <summary>
        /// 入力された参加コードを比較可能な 12 文字へ正規化する（§1.6）。
        /// 手順: NFKC 正規化 → 前後空白の除去 → 大文字化 → ハイフン・空白の除去 → O/I/L の読み替え → 12 文字チェック。
        /// </summary>
        /// <exception cref="JoinCodeException">正規化結果が 12 文字でないとき（<c>null</c> を含む）。</exception>
        public static string Normalize(string input)
        {
            if (!TryNormalize(input, out var normalized, out var error, out var message))
            {
                throw new JoinCodeException(error, message);
            }

            return normalized;
        }

        /// <summary>
        /// 入力途中の参加コードを、長さのチェックをせずに正規化だけ行う（§1.6 の手順 0〜4）。
        /// UI が「入力のたびに正規化してプレビュー表示」する（docs/network.md §2.2 の 1）ために使う。
        /// <see cref="Normalize"/> と異なり 12 文字に満たない・超える入力でも例外を投げない。
        /// </summary>
        /// <param name="input">入力途中の参加コード（null 可）。</param>
        /// <param name="normalized">
        /// 正規化結果（前後空白除去・大文字化・ハイフン/空白除去・O/I/L の読み替え済み）。
        /// <paramref name="input"/> が null のときは空文字。長さのチェックはしないため 12 文字とは限らない。
        /// </param>
        /// <returns>
        /// 正規化できたら true。不正な Unicode（対になっていないサロゲートなど）を含む場合は false。
        /// </returns>
        public static bool TryNormalizePartial(string input, out string normalized)
        {
            if (input == null)
            {
                normalized = string.Empty;
                return true;
            }

            // 対になっていないサロゲートは事前に自前で検出する。string.Normalize() の例外送出だけに
            // 頼ると、ランタイム（Mono / CoreCLR 等）によって挙動が変わり得るため（実測で確認）、
            // 判定を自前のスキャンで確定させたうえで、Normalize() 自体の失敗は保険として残す。
            if (!IsWellFormedUtf16(input))
            {
                normalized = string.Empty;
                return false;
            }

            string compatibility;
            try
            {
                compatibility = input.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                normalized = string.Empty;
                return false;
            }

            var upper = compatibility.Trim().ToUpperInvariant();

            var builder = new StringBuilder(upper.Length);
            foreach (var ch in upper)
            {
                if (ch == GroupSeparator || char.IsWhiteSpace(ch))
                {
                    continue;
                }

                builder.Append(ToDecodeAlias(ch));
            }

            normalized = builder.ToString();
            return true;
        }

        /// <summary>デコード本体。失敗理由とユーザー向け文言を返し、例外は投げない。</summary>
        private static bool TryDecodeInternal(
            string code,
            out (int Version, string Ip, int Port) result,
            out JoinCodeError error,
            out string message)
        {
            result = default;

            if (!TryNormalize(code, out var normalized, out error, out message))
            {
                return false;
            }

            ulong w = 0;
            foreach (var ch in normalized)
            {
                var index = Alphabet.IndexOf(ch);
                if (index < 0)
                {
                    // M-2: メッセージは JoinCodeErrorMessages を単一の情報源とする（どの文字が
                    // 不正だったかの詳細は落ちるが、UI 表示・例外メッセージ・テストで文言が食い違わない）。
                    error = JoinCodeError.InvalidCharacter;
                    message = JoinCodeErrorMessages.Create(error);
                    return false;
                }

                w = (w << BitsPerChar) | (ulong)(uint)index;
            }

            var check = w & CheckMask;
            if (check >= CheckMod)
            {
                // 1021〜1023 は正規のエンコーダーが生成し得ない値なので、計算するまでもなく拒否する（§1.5）。
                error = JoinCodeError.CheckOutOfRange;
                message = JoinCodeErrorMessages.Create(error);
                return false;
            }

            var v = w >> CheckBits;
            if (v % CheckMod != check)
            {
                error = JoinCodeError.ChecksumMismatch;
                message = JoinCodeErrorMessages.Create(error);
                return false;
            }

            var version = (int)((v >> VersionShift) & MaxVersion);
            var ipv4 = (uint)((v >> Ipv4Shift) & 0xFFFFFFFFUL);
            var port = (int)(v & 0xFFFFUL);

            result = (version, FormatIpv4(ipv4), port);
            error = JoinCodeError.None;
            message = null;
            return true;
        }

        /// <summary>
        /// 正規化本体（§1.6）。失敗理由とユーザー向け文言を返し、例外は投げない。
        /// M-3: 手順 0〜4（NFKC・トリム・大文字化・ハイフン/空白除去・O/I/L 読み替え）は
        /// <see cref="TryNormalizePartial"/> に委譲し、ここでは手順 5（12 文字チェック）だけを追加で行う
        /// （同じ正規化ロジックを 2 か所に重複させない）。
        /// </summary>
        private static bool TryNormalize(string input, out string normalized, out JoinCodeError error, out string message)
        {
            normalized = null;

            if (!TryNormalizePartial(input, out var partial))
            {
                // 不正な Unicode（対になっていないサロゲートなど）は正規化できない。
                error = JoinCodeError.InvalidCharacter;
                message = JoinCodeErrorMessages.Create(error);
                return false;
            }

            // 手順 5: 12 文字でなければエラー（null 入力は partial が空文字になり、ここで弾かれる）。
            if (partial.Length != CodeLength)
            {
                error = JoinCodeError.InvalidLength;
                message = JoinCodeErrorMessages.Create(error);
                return false;
            }

            normalized = partial;
            error = JoinCodeError.None;
            message = null;
            return true;
        }

        /// <summary>
        /// 対になっていないサロゲート（単独の高位/低位サロゲート）を含まないかを調べる。
        /// <see cref="PlayerNameValidator"/> の同種のチェックと同じ考え方（高位→低位の対のみ許可）。
        /// </summary>
        private static bool IsWellFormedUtf16(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (char.IsHighSurrogate(ch))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    {
                        return false;
                    }

                    i++;
                }
                else if (char.IsLowSurrogate(ch))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Crockford のデコード別名（O→0、I→1、L→1）。</summary>
        private static char ToDecodeAlias(char ch)
        {
            switch (ch)
            {
                case 'O':
                    return '0';
                case 'I':
                case 'L':
                    return '1';
                default:
                    return ch;
            }
        }

        /// <summary>60bit の W を Base32 の 12 文字へ変換する。</summary>
        private static char[] ToBody(ulong w)
        {
            var body = new char[CodeLength];
            for (var i = 0; i < CodeLength; i++)
            {
                var shift = BitsPerChar * (CodeLength - 1 - i);
                body[i] = Alphabet[(int)((w >> shift) & 31UL)];
            }

            return body;
        }

        /// <summary>12 文字を表示形式（XXXX-XXXX-XXXX）へ整形する。</summary>
        private static string Format(char[] body)
        {
            var builder = new StringBuilder(CodeLength + (CodeLength / GroupLength) - 1);
            for (var i = 0; i < CodeLength; i++)
            {
                if (i > 0 && i % GroupLength == 0)
                {
                    builder.Append(GroupSeparator);
                }

                builder.Append(body[i]);
            }

            return builder.ToString();
        }

        /// <summary>ドット区切りの IPv4 アドレスを 32bit 整数へ変換する。</summary>
        /// <exception cref="JoinCodeException">形式が不正、またはオクテットが 0〜255 の範囲外のとき。</exception>
        private static uint ParseIpv4(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                throw new JoinCodeException(
                    JoinCodeError.InvalidAddress,
                    "IPv4 アドレスが指定されていません。");
            }

            var parts = ip.Split('.');
            if (parts.Length != OctetCount)
            {
                throw new JoinCodeException(
                    JoinCodeError.InvalidAddress,
                    "IPv4 アドレスの形式が正しくありません（a.b.c.d 形式で指定してください）: " + ip);
            }

            uint ipv4 = 0;
            foreach (var part in parts)
            {
                // 先頭 0 のオクテット（"007" など）は 8 進数と解釈される処理系があり曖昧なため、正準形のみ受け付ける。
                var hasRedundantZero = part.Length > 1 && part[0] == '0';
                if (hasRedundantZero ||
                    !byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var octet))
                {
                    throw new JoinCodeException(
                        JoinCodeError.InvalidAddress,
                        "IPv4 アドレスの各オクテットは 0〜255 の整数である必要があります: " + ip);
                }

                ipv4 = (ipv4 << 8) | octet;
            }

            return ipv4;
        }

        /// <summary>32bit 整数をドット区切りの IPv4 アドレスへ変換する。</summary>
        private static string FormatIpv4(uint ipv4)
        {
            var builder = new StringBuilder(15);
            for (var shift = 24; shift >= 0; shift -= 8)
            {
                if (shift != 24)
                {
                    builder.Append('.');
                }

                builder.Append(((ipv4 >> shift) & 255U).ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
