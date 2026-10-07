using System;
using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// プリセット / アプリ設定 JSON をキー単位で読み取るための共通ヘルパー（#26 統括判断 M9/M10）。
    /// 個々のキーの型が期待と異なる場合でも、そのキーだけを既定値扱いにして警告を追加し、
    /// JSON 全体のパース失敗（<see cref="RoomSettingsInput"/> 全体が既定値に落ちる）を避ける。
    /// 読み取った以外の未知キーは <see cref="GetUnknownKeys"/> で検出できる（M9）。
    /// </summary>
    /// <remarks>
    /// 数値の取得（<see cref="GetInt"/> / <see cref="GetLong"/> / <see cref="GetDouble"/>）は、
    /// <c>int</c>/<c>long</c> の範囲を超える整数（Newtonsoft が <see cref="BigInteger"/> として保持する）を
    /// 明示的に検出して警告 + null にする。<see cref="JValue.Value{T}()"/> にそのまま丸投げすると
    /// <see cref="OverflowException"/> / <see cref="InvalidCastException"/> を投げてキー単位の回復ができなくなるため
    /// （#26 統括判断 H1）。
    /// </remarks>
    internal sealed class JsonInputReader
    {
        private readonly JObject _obj;
        private readonly List<string> _warnings;
        private readonly HashSet<string> _consumedKeys = new HashSet<string>();

        public JsonInputReader(JObject obj, List<string> warnings)
        {
            _obj = obj ?? new JObject();
            _warnings = warnings;
        }

        public string GetString(string key)
        {
            _consumedKeys.Add(key);
            if (!_obj.TryGetValue(key, out var token) || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            WarnTypeMismatch(key, token);
            return null;
        }

        public bool? GetBool(string key)
        {
            _consumedKeys.Add(key);
            if (!_obj.TryGetValue(key, out var token) || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            WarnTypeMismatch(key, token);
            return null;
        }

        /// <summary>
        /// <c>int</c> として読み取る。<c>int</c> の範囲外（<see cref="long"/> や <see cref="BigInteger"/> でしか
        /// 表現できない値）は警告を追加して null を返す（H1）。
        /// </summary>
        public int? GetInt(string key)
        {
            _consumedKeys.Add(key);
            if (!_obj.TryGetValue(key, out var token) || token.Type == JTokenType.Null)
            {
                return null;
            }

            try
            {
                if (token.Type == JTokenType.Integer && token is JValue value)
                {
                    if (value.Value is long longValue)
                    {
                        if (longValue >= int.MinValue && longValue <= int.MaxValue)
                        {
                            return (int)longValue;
                        }

                        WarnOutOfRange(key, longValue.ToString());
                        return null;
                    }

                    if (value.Value is BigInteger bigValue)
                    {
                        WarnOutOfRange(key, bigValue.ToString());
                        return null;
                    }
                }
            }
            catch (Exception ex) when (IsNumericConversionException(ex))
            {
                WarnTypeMismatch(key, token);
                return null;
            }

            WarnTypeMismatch(key, token);
            return null;
        }

        /// <summary>
        /// <c>long</c> として読み取る。<c>long</c> の範囲外（<see cref="BigInteger"/> でしか表現できない値）は
        /// 警告を追加して null を返す（H1）。
        /// </summary>
        public long? GetLong(string key)
        {
            _consumedKeys.Add(key);
            if (!_obj.TryGetValue(key, out var token) || token.Type == JTokenType.Null)
            {
                return null;
            }

            try
            {
                if (token.Type == JTokenType.Integer && token is JValue value)
                {
                    if (value.Value is long longValue)
                    {
                        return longValue;
                    }

                    if (value.Value is BigInteger bigValue)
                    {
                        WarnOutOfRange(key, bigValue.ToString());
                        return null;
                    }
                }
            }
            catch (Exception ex) when (IsNumericConversionException(ex))
            {
                WarnTypeMismatch(key, token);
                return null;
            }

            WarnTypeMismatch(key, token);
            return null;
        }

        /// <summary>
        /// <c>double</c> として読み取る。<c>long</c> / <c>double</c> / <c>decimal</c> のいずれかを明示的に変換する。
        /// <see cref="BigInteger"/>（<c>long</c> の範囲外の整数）は警告を追加して null を返す（H1）。
        /// </summary>
        public double? GetDouble(string key)
        {
            _consumedKeys.Add(key);
            if (!_obj.TryGetValue(key, out var token) || token.Type == JTokenType.Null)
            {
                return null;
            }

            try
            {
                if (token is JValue value)
                {
                    if (token.Type == JTokenType.Integer)
                    {
                        if (value.Value is long longValue)
                        {
                            return longValue;
                        }

                        if (value.Value is BigInteger bigValue)
                        {
                            WarnOutOfRange(key, bigValue.ToString());
                            return null;
                        }
                    }
                    else if (token.Type == JTokenType.Float)
                    {
                        if (value.Value is double doubleValue)
                        {
                            return doubleValue;
                        }

                        if (value.Value is decimal decimalValue)
                        {
                            return (double)decimalValue;
                        }
                    }
                }
            }
            catch (Exception ex) when (IsNumericConversionException(ex))
            {
                WarnTypeMismatch(key, token);
                return null;
            }

            WarnTypeMismatch(key, token);
            return null;
        }

        public List<string> GetStringList(string key)
        {
            _consumedKeys.Add(key);
            if (!_obj.TryGetValue(key, out var token) || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type != JTokenType.Array)
            {
                WarnTypeMismatch(key, token);
                return null;
            }

            var list = new List<string>();
            foreach (var item in (JArray)token)
            {
                if (item.Type == JTokenType.String)
                {
                    list.Add(item.Value<string>());
                }
                else
                {
                    _warnings.Add($"{key} の要素に文字列以外の値が含まれていたため、その要素を無視しました。");
                }
            }

            return list;
        }

        /// <summary>本メソッド呼び出しまでに読み取っていない（＝スキーマに無い）トップレベルキー一覧（M9）。</summary>
        public IReadOnlyList<string> GetUnknownKeys()
        {
            var unknown = new List<string>();
            foreach (var prop in _obj.Properties())
            {
                if (!_consumedKeys.Contains(prop.Name))
                {
                    unknown.Add(prop.Name);
                }
            }

            return unknown;
        }

        private void WarnTypeMismatch(string key, JToken token)
        {
            _warnings.Add($"{key} の型が不正です（{token.Type}）。既定値を使用します。");
        }

        private void WarnOutOfRange(string key, string rawValue)
        {
            _warnings.Add($"{key} の値 {rawValue} は表現できる範囲外のため既定値を使用します。");
        }

        /// <summary>
        /// 数値変換で投げうる例外か（保険。上の明示分岐で通常は発生しないが、想定外の入力で
        /// <see cref="JValue.Value{T}()"/> 相当の変換が失敗した場合もキー単位で回復させる、H1）。
        /// </summary>
        private static bool IsNumericConversionException(Exception ex) =>
            ex is OverflowException || ex is InvalidCastException || ex is FormatException;
    }
}
