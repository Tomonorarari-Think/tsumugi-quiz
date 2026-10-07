using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TsumugiQuiz.Questions;
using Unity.Netcode;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// クライアントへ配信する問題データ（docs/question-data.md §7、docs/network.md §8.1）。
    /// <see cref="Question"/> から正解情報を落としたもので、
    /// <c>answers</c> / <c>correctIndex</c> に相当するフィールドを**構造上持たない**。
    /// 「送信時に詰め忘れる」ではなく「そもそも積めない」形にするのが本型の目的（仮決め K14）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 実質不変。公開しているのは読み取り専用のプロパティだけで、値は生成時（<see cref="From"/> か
    /// コンストラクタ）か受信時（<see cref="NetworkSerialize{T}"/> の読み取り）にしか入らない。
    /// 引数なしコンストラクタは NGO の受信側（<c>FastBufferReader.ReadNetworkSerializable</c> が
    /// <c>new T()</c> する）のために公開している。
    /// </para>
    /// <para>
    /// 画像（<c>imageData</c>）は本型に含めない。分割送信（docs/network.md §8.3）で別途送るため、#16 で追加する。
    /// </para>
    /// <para>
    /// サイズ上限は送信側（<c>QuestionDistributor</c>）で送信前に、受信側でも受信直後に
    /// <see cref="TryValidate"/> で検証する（docs/network.md §9「ネットワーク受信データは必ず検証する」）。
    /// </para>
    /// </remarks>
    public sealed class QuestionDto : INetworkSerializable
    {
        // 上限値は Questions 層の QuestionLimits（docs/question-data.md §1 / §2）を単一の出所として参照する。
        // 読み込み時の検証（QuestionSetValidator）と同じ値になるため、
        // 「読み込めたのに配信できない問題」が生まれない。

        /// <summary>問題 ID の最大文字数。</summary>
        public const int MaxIdLength = QuestionLimits.MaxIdLength;

        /// <summary>問題文の最大文字数。</summary>
        public const int MaxTextLength = QuestionLimits.MaxTextLength;

        /// <summary>読み上げ用テキストの最大文字数。</summary>
        public const int MaxReadingTextLength = QuestionLimits.MaxReadingTextLength;

        /// <summary>選択肢の最小件数（<c>choice</c> のとき）。</summary>
        public const int MinChoiceCount = QuestionLimits.MinChoiceCount;

        /// <summary>選択肢の最大件数。</summary>
        public const int MaxChoiceCount = QuestionLimits.MaxChoiceCount;

        /// <summary>選択肢 1 件の最大文字数。</summary>
        public const int MaxChoiceLength = QuestionLimits.MaxChoiceLength;

        /// <summary>タグの最大件数。</summary>
        public const int MaxTagCount = QuestionLimits.MaxTagCount;

        /// <summary>タグ 1 件の最大文字数。</summary>
        public const int MaxTagLength = QuestionLimits.MaxTagLength;

        /// <summary>難易度の下限。</summary>
        public const int MinDifficulty = QuestionLimits.MinDifficulty;

        /// <summary>難易度の上限。</summary>
        public const int MaxDifficulty = QuestionLimits.MaxDifficulty;

        private static readonly string[] EmptyStrings = Array.Empty<string>();

        private string _id = string.Empty;
        private QuestionType _type = QuestionType.FreeText;
        private string _text = string.Empty;
        private string _readingText = string.Empty;
        private string[] _choices = EmptyStrings;
        private int _difficulty = Question.DefaultDifficulty;
        private string[] _tags = EmptyStrings;

        /// <summary>公開用の読み取り専用ビュー（内部配列を書き換えられないようにするため）。</summary>
        private ReadOnlyCollection<string> _choicesView;
        private ReadOnlyCollection<string> _tagsView;

        /// <summary>
        /// 受信データが構造的に壊れていたか（件数が上限を超えていた等）。
        /// 壊れた入力で巨大な配列を確保しないよう、読み取りを打ち切ったときに立てる。
        /// </summary>
        private bool _malformed;

        /// <summary>NGO の受信（<c>new T()</c>）用。アプリのコードからは <see cref="From"/> を使う。</summary>
        public QuestionDto()
        {
        }

        /// <summary>
        /// 値を指定して生成する。
        /// </summary>
        /// <param name="id">問題 ID。</param>
        /// <param name="type">出題形式。</param>
        /// <param name="text">画面表示用の問題文。</param>
        /// <param name="readingText">読み上げ用テキスト（空なら <paramref name="text"/> を埋める）。</param>
        /// <param name="choices">選択肢（<c>choice</c> のときのみ）。null は空配列扱い。</param>
        /// <param name="difficulty">難易度。</param>
        /// <param name="tags">タグ。null は空配列扱い。</param>
        public QuestionDto(
            string id,
            QuestionType type,
            string text,
            string readingText = null,
            IReadOnlyList<string> choices = null,
            int difficulty = Question.DefaultDifficulty,
            IReadOnlyList<string> tags = null)
        {
            _id = id ?? string.Empty;
            _type = type;
            _text = text ?? string.Empty;
            _readingText = string.IsNullOrEmpty(readingText) ? _text : readingText;
            _choices = Copy(choices);
            _difficulty = difficulty;
            _tags = Copy(tags);
        }

        /// <summary>問題 ID。</summary>
        public string Id => _id;

        /// <summary>出題形式。</summary>
        public QuestionType Type => _type;

        /// <summary>画面表示用の問題文。</summary>
        public string Text => _text;

        /// <summary>読み上げ用テキスト（省略された問題では <see cref="Text"/> と同じ値が入る）。</summary>
        public string ReadingText => _readingText;

        /// <summary>選択肢。<c>freeText</c> では空。内部配列は公開しない。</summary>
        public IReadOnlyList<string> Choices => _choicesView ??= Array.AsReadOnly(_choices);

        /// <summary>難易度（参考表示用）。</summary>
        public int Difficulty => _difficulty;

        /// <summary>タグ（参考表示用）。内部配列は公開しない。</summary>
        public IReadOnlyList<string> Tags => _tagsView ??= Array.AsReadOnly(_tags);

        /// <summary>
        /// 問題から配信用 DTO を作る（サーバーのみが呼ぶ）。
        /// 正解（<see cref="Question.Answers"/> / <see cref="Question.CorrectIndex"/>）は写さない。
        /// </summary>
        /// <param name="question">元の問題。</param>
        /// <returns>配信用 DTO。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="question"/> が null のとき。</exception>
        /// <exception cref="ArgumentException"><paramref name="question"/> の出題形式が未設定のとき。</exception>
        public static QuestionDto From(Question question) => From(question, out _);

        /// <summary>
        /// 問題から配信用 DTO を作る。参考表示用の <c>tags</c> は上限
        /// （<see cref="MaxTagCount"/> 件 × <see cref="MaxTagLength"/> 文字）を超えていたら
        /// 切り詰める。出題そのものを止めないための措置で、切り詰めたかどうかを
        /// <paramref name="tagsTruncated"/> で返す（呼び出し側が警告を残す）。
        /// </summary>
        /// <param name="question">元の問題。</param>
        /// <param name="tagsTruncated">タグを切り詰めたか。</param>
        /// <returns>配信用 DTO。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="question"/> が null のとき。</exception>
        /// <exception cref="ArgumentException"><paramref name="question"/> の出題形式が未設定のとき。</exception>
        public static QuestionDto From(Question question, out bool tagsTruncated)
        {
            if (question == null)
            {
                throw new ArgumentNullException(nameof(question));
            }

            if (!question.Type.HasValue)
            {
                // type 未設定の問題は QuestionSetValidator が読み込み時に弾くため、ここに来るのは呼び出し側の不具合。
                throw new ArgumentException(
                    $"出題形式（type）が設定されていない問題は配信できません（id: {question.Id}）。", nameof(question));
            }

            var tags = TrimTags(question.Tags, out tagsTruncated);

            return new QuestionDto(
                question.Id,
                question.Type.Value,
                question.Text,
                question.EffectiveReadingText,
                question.Choices,
                question.Difficulty,
                tags);
        }

        /// <summary>タグを上限（件数・1 件の長さ）に収める。超過分は捨て、長すぎるタグは切り詰める。</summary>
        private static string[] TrimTags(IReadOnlyList<string> tags, out bool truncated)
        {
            truncated = false;
            if (tags == null || tags.Count == 0)
            {
                return EmptyStrings;
            }

            var count = Math.Min(tags.Count, MaxTagCount);
            truncated = tags.Count > MaxTagCount;

            var trimmed = new string[count];
            for (var i = 0; i < count; i++)
            {
                var tag = tags[i] ?? string.Empty;
                if (tag.Length > MaxTagLength)
                {
                    tag = tag.Substring(0, MaxTagLength);
                    truncated = true;
                }

                trimmed[i] = tag;
            }

            return trimmed;
        }

        /// <summary>
        /// 配信してよい内容かを検証する（送信前・受信直後の両方で呼ぶ）。
        /// </summary>
        /// <param name="error">不正だった理由（ログ用の日本語。クライアントへは返さない）。</param>
        /// <returns>妥当なら true。</returns>
        public bool TryValidate(out string error)
        {
            if (_malformed)
            {
                error = "受信データの件数が上限を超えていたため読み取りを打ち切りました。";
                return false;
            }

            if (string.IsNullOrEmpty(_id))
            {
                error = "id が空です。";
                return false;
            }

            if (_id.Length > MaxIdLength)
            {
                error = $"id が {MaxIdLength} 文字を超えています（実際: {_id.Length} 文字）。";
                return false;
            }

            if (!Enum.IsDefined(typeof(QuestionType), _type))
            {
                error = $"出題形式が不正です（値: {(int)_type}）。";
                return false;
            }

            if (string.IsNullOrEmpty(_text))
            {
                error = "text が空です。";
                return false;
            }

            if (_text.Length > MaxTextLength)
            {
                error = $"text が {MaxTextLength} 文字を超えています（実際: {_text.Length} 文字）。";
                return false;
            }

            if (string.IsNullOrEmpty(_readingText))
            {
                error = "readingText が空です（省略時は text を埋めて送る必要があります）。";
                return false;
            }

            if (_readingText.Length > MaxReadingTextLength)
            {
                error = $"readingText が {MaxReadingTextLength} 文字を超えています（実際: {_readingText.Length} 文字）。";
                return false;
            }

            if (!TryValidateChoices(out error))
            {
                return false;
            }

            if (_difficulty < MinDifficulty || _difficulty > MaxDifficulty)
            {
                error = $"difficulty は{MinDifficulty}〜{MaxDifficulty}の範囲である必要があります（実際: {_difficulty}）。";
                return false;
            }

            return TryValidateTags(out error);
        }

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            if (serializer.IsReader)
            {
                // 受信で配列が差し替わるため、公開用ビューは作り直す。
                _choicesView = null;
                _tagsView = null;
            }

            serializer.SerializeValue(ref _id);
            serializer.SerializeValue(ref _type);
            serializer.SerializeValue(ref _text);
            serializer.SerializeValue(ref _readingText);
            SerializeStrings(serializer, ref _choices, MaxChoiceCount, ref _malformed);

            if (_malformed)
            {
                // 以降のバイト列は信用できないので読み進めない（TryValidate が不正として弾く）。
                // 書き込み時は _malformed が立たないため、この分岐は受信時にしか効かない。
                return;
            }

            serializer.SerializeValue(ref _difficulty);
            SerializeStrings(serializer, ref _tags, MaxTagCount, ref _malformed);
        }

        /// <summary>
        /// 文字列配列を「件数 + 要素」の形で読み書きする。
        /// 受信時は件数を先に検証し、上限を超える件数では配列を確保せずに打ち切る
        /// （壊れた・悪意ある長さでメモリを食い潰さないため。docs/network.md §9）。
        /// </summary>
        private static void SerializeStrings<T>(
            BufferSerializer<T> serializer, ref string[] values, int maxCount, ref bool malformed)
            where T : IReaderWriter
        {
            if (serializer.IsWriter)
            {
                var count = values?.Length ?? 0;
                serializer.SerializeValue(ref count);
                for (var i = 0; i < count; i++)
                {
                    var item = values[i] ?? string.Empty;
                    serializer.SerializeValue(ref item);
                }

                return;
            }

            var receivedCount = 0;
            serializer.SerializeValue(ref receivedCount);
            if (receivedCount < 0 || receivedCount > maxCount)
            {
                malformed = true;
                values = EmptyStrings;
                return;
            }

            var buffer = receivedCount == 0 ? EmptyStrings : new string[receivedCount];
            for (var i = 0; i < receivedCount; i++)
            {
                var item = string.Empty;
                serializer.SerializeValue(ref item);
                buffer[i] = item ?? string.Empty;
            }

            values = buffer;
        }

        private bool TryValidateChoices(out string error)
        {
            if (_choices.Length > MaxChoiceCount)
            {
                error = $"choices は{MaxChoiceCount}件以内である必要があります（実際: {_choices.Length}件）。";
                return false;
            }

            if (_type == QuestionType.Choice && _choices.Length < MinChoiceCount)
            {
                error = $"choice 形式の choices は{MinChoiceCount}件以上である必要があります（実際: {_choices.Length}件）。";
                return false;
            }

            if (_type == QuestionType.FreeText && _choices.Length > 0)
            {
                error = $"freeText 形式で choices が指定されています（実際: {_choices.Length}件）。";
                return false;
            }

            for (var i = 0; i < _choices.Length; i++)
            {
                var choice = _choices[i];
                if (string.IsNullOrEmpty(choice))
                {
                    error = $"choices[{i}] が空です。";
                    return false;
                }

                if (choice.Length > MaxChoiceLength)
                {
                    error = $"choices[{i}] が {MaxChoiceLength} 文字を超えています（実際: {choice.Length} 文字）。";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private bool TryValidateTags(out string error)
        {
            if (_tags.Length > MaxTagCount)
            {
                error = $"tags は{MaxTagCount}件以内である必要があります（実際: {_tags.Length}件）。";
                return false;
            }

            for (var i = 0; i < _tags.Length; i++)
            {
                var tag = _tags[i];
                if (tag == null)
                {
                    error = $"tags[{i}] が null です。";
                    return false;
                }

                if (tag.Length > MaxTagLength)
                {
                    error = $"tags[{i}] が {MaxTagLength} 文字を超えています（実際: {tag.Length} 文字）。";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static string[] Copy(IReadOnlyList<string> source)
        {
            if (source == null || source.Count == 0)
            {
                return EmptyStrings;
            }

            var copied = new string[source.Count];
            for (var i = 0; i < source.Count; i++)
            {
                copied[i] = source[i] ?? string.Empty;
            }

            return copied;
        }
    }
}
