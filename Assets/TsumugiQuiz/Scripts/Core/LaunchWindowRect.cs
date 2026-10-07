using System.Globalization;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <c>-tq-window</c> 引数（"x,y,w,h"）が表すウィンドウの位置・サイズ（純 C#、不変）。
    /// 同一 PC で複数プロセスを起動して手動検証する際、各ウィンドウを画面上に並べるために使う
    /// （docs/network.md §10.3、issue #8）。
    /// </summary>
    public readonly struct LaunchWindowRect
    {
        public LaunchWindowRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>ウィンドウ左上の X 座標（仮想デスクトップ座標）。</summary>
        public int X { get; }

        /// <summary>ウィンドウ左上の Y 座標（仮想デスクトップ座標）。</summary>
        public int Y { get; }

        /// <summary>ウィンドウ幅（ピクセル）。1〜<see cref="MaxDimension"/>。</summary>
        public int Width { get; }

        /// <summary>ウィンドウ高さ（ピクセル）。1〜<see cref="MaxDimension"/>。</summary>
        public int Height { get; }

        /// <summary>
        /// 幅・高さの上限（ピクセル）。レビュー L-4: 現実的などのモニタ構成でもあり得ない極端な値
        /// （桁の打ち間違い等）を弾く。8K ディスプレイ横並び程度を想定して余裕を持たせた値。
        /// </summary>
        public const int MaxDimension = 16384;

        /// <summary>
        /// "x,y,w,h" 形式の文字列を解析する。区切りはカンマのみ受理し、各要素の前後の空白は許容する。
        /// 要素数が 4 でない、整数として読めない、幅・高さが 1 未満または <see cref="MaxDimension"/> を
        /// 超える場合は false を返す（不正な値でウィンドウ操作を試みて起動を不安定にしないため）。
        /// </summary>
        public static bool TryParse(string raw, out LaunchWindowRect rect)
        {
            rect = default;

            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var parts = raw.Split(',');
            if (parts.Length != 4)
            {
                return false;
            }

            if (!TryParseInt(parts[0], out var x) ||
                !TryParseInt(parts[1], out var y) ||
                !TryParseInt(parts[2], out var width) ||
                !TryParseInt(parts[3], out var height))
            {
                return false;
            }

            if (width < 1 || height < 1 || width > MaxDimension || height > MaxDimension)
            {
                return false;
            }

            rect = new LaunchWindowRect(x, y, width, height);
            return true;
        }

        private static bool TryParseInt(string raw, out int value)
            => int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
