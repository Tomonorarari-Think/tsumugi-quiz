using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// クライアントごとの得点表（docs/room-settings.md §1「得点」、docs/network.md §6.6）。
    /// <b>不変</b>で、更新は新しいインスタンスを返す（<see cref="WithCorrect"/> / <see cref="WithWrong"/>）。
    /// </summary>
    /// <remarks>
    /// 加点・減点の値は <see cref="ScoreRules"/> が持つ。得点は負になりうる（減点ペナルティ）。
    /// 得点 0 のクライアントも「回答した事実」を残すため表に載せる（<see cref="WithDelta"/>）。
    /// </remarks>
    public sealed class ScoreBoard
    {
        private readonly Dictionary<ulong, int> _scores;
        private readonly ReadOnlyCollection<ulong> _clientIdsView;

        /// <summary>
        /// 空の得点表を生成する。
        /// </summary>
        /// <param name="rules">得点規則。null なら <see cref="ScoreRules.Default"/>。</param>
        public ScoreBoard(ScoreRules rules = null)
            : this(rules ?? ScoreRules.Default, new Dictionary<ulong, int>())
        {
        }

        private ScoreBoard(ScoreRules rules, Dictionary<ulong, int> scores)
        {
            Rules = rules;
            _scores = scores;

            // 列挙順を決定的にするため、常にクライアント ID の昇順で見せる。
            var ids = new List<ulong>(scores.Keys);
            ids.Sort();
            _clientIdsView = new ReadOnlyCollection<ulong>(ids);
        }

        /// <summary>既定の得点規則を持つ空の得点表。</summary>
        public static ScoreBoard Empty { get; } = new ScoreBoard(ScoreRules.Default, new Dictionary<ulong, int>());

        /// <summary>得点規則。</summary>
        public ScoreRules Rules { get; }

        /// <summary>表に載っているクライアント数。</summary>
        public int Count => _scores.Count;

        /// <summary>表に載っているクライアント ID（昇順）。</summary>
        public IReadOnlyList<ulong> ClientIds => _clientIdsView;

        /// <summary>指定クライアントの得点。表に無ければ 0。</summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <returns>得点。</returns>
        public int GetScore(ulong clientId) => _scores.TryGetValue(clientId, out var score) ? score : 0;

        /// <summary>指定クライアントが表に載っているか。</summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <returns>載っていたら true。</returns>
        public bool Contains(ulong clientId) => _scores.ContainsKey(clientId);

        /// <summary>正解として加点した新しい得点表を返す。</summary>
        /// <param name="clientId">正解したクライアント ID。</param>
        /// <returns>新しいインスタンス。</returns>
        public ScoreBoard WithCorrect(ulong clientId) => WithDelta(clientId, Rules.CorrectDelta);

        /// <summary>誤答（お手つき）として得点を更新した新しい得点表を返す。</summary>
        /// <param name="clientId">誤答したクライアント ID。</param>
        /// <returns>新しいインスタンス。</returns>
        public ScoreBoard WithWrong(ulong clientId) => WithDelta(clientId, Rules.WrongDelta);

        /// <summary>
        /// 任意の差分を加えた新しい得点表を返す。差分が 0 でも表にはエントリを作る。
        /// </summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <param name="delta">加える差分。</param>
        /// <returns>新しいインスタンス。</returns>
        public ScoreBoard WithDelta(ulong clientId, int delta)
        {
            var next = new Dictionary<ulong, int>(_scores);
            next[clientId] = GetScore(clientId) + delta;
            return new ScoreBoard(Rules, next);
        }

        /// <summary>得点規則だけを差し替えた新しい得点表を返す（得点は保持する）。</summary>
        /// <param name="rules">新しい得点規則。</param>
        /// <returns>新しいインスタンス。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rules"/> が null のとき。</exception>
        public ScoreBoard WithRules(ScoreRules rules)
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            return new ScoreBoard(rules, new Dictionary<ulong, int>(_scores));
        }

        /// <summary>
        /// 得点行のキー（クライアント ID）を付け替えた新しい得点表を返す（#84、再接続の引き継ぎ）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// NGO はクライアント ID を使い回さない（NGO 2.13.2
        /// <c>Runtime/Connection/NetworkConnectionManager.cs</c> L350 / L531 の <c>m_NextClientId++</c>）ため、
        /// 同じ人が再接続すると得点表のキーだけがずれる。席（名簿エントリ）が同一であることを
        /// 上位層（<c>LobbyRoster</c> の再接続トークン判定、#69）が保証したうえで本メソッドを呼ぶ。
        /// </para>
        /// <para>
        /// <paramref name="fromClientId"/> の行が無ければ何もしない（同じインスタンスを返す）。
        /// <paramref name="toClientId"/> に既に行がある場合は移動元の値で<b>上書き</b>する
        /// （新しいクライアント ID は接続したばかりで得点を持たないため、通常は起こらない。
        /// 万一起きた場合は「席が持っている得点」を権威とする）。
        /// </para>
        /// </remarks>
        /// <param name="fromClientId">切断時に使っていた古いクライアント ID。</param>
        /// <param name="toClientId">復帰後の新しいクライアント ID。</param>
        /// <returns>新しいインスタンス（付け替えが不要なら同じインスタンス）。</returns>
        public ScoreBoard WithClientIdChanged(ulong fromClientId, ulong toClientId)
        {
            if (fromClientId == toClientId || !_scores.TryGetValue(fromClientId, out var score))
            {
                return this;
            }

            var next = new Dictionary<ulong, int>(_scores);
            next.Remove(fromClientId);
            next[toClientId] = score;
            return new ScoreBoard(Rules, next);
        }

        /// <summary>得点規則を保ったまま空にした新しい得点表を返す。</summary>
        /// <returns>新しいインスタンス。</returns>
        public ScoreBoard Cleared() => new ScoreBoard(Rules, new Dictionary<ulong, int>());
    }
}
