using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 早押しの押下イベントをサーバー時刻基準で裁定する純ロジック（docs/network.md §6.3〜§6.5）。
    /// Unity API に依存せず、時刻はすべて「サーバー時刻軸の double 秒」で受け取る。
    /// 1 回の早押し受付につき 1 インスタンスを生成して使い捨てる。
    /// </summary>
    /// <remarks>
    /// 判定は絶対時刻ではなく受付開始 T0 からの相対経過 <c>dt</c> で行う。
    /// クライアントは自分の <c>LocalTime</c>（サーバーで処理される時刻の推定値）を報告するため、
    /// 片道遅延は T0 側にも同じだけ乗っており、差分を取ると人間の反応時間だけが残る（§6.5）。
    /// スレッドセーフではない。サーバーのメインループからのみ呼ぶこと。
    /// </remarks>
    public sealed class BuzzArbiter
    {
        /// <summary>同着とみなす閾値（秒）。NGO の tick 粒度より十分小さく、設定項目にはしない（§6.3）。</summary>
        public const double DefaultTieEpsilonSec = 0.001;

        /// <summary>サーバー受信時刻からこれ以上先の時刻を名乗る押下は棄却する（秒、§6.4）。</summary>
        public const double MaxFutureSec = 1.0;

        /// <summary>
        /// 受付開始 T0 からこれ以上前の時刻を名乗る押下は棄却する（秒、§6.4）。
        /// NGO の tick 粒度 33.3ms に余裕を持たせた値。これ以内のずれだけを T0 に丸める。
        /// </summary>
        public const double MaxPastSec = 0.05;

        private readonly double _t0;
        private readonly double _collectWindowSec;
        private readonly double _tieEpsilonSec;
        private readonly HashSet<ulong> _penalized;
        private readonly List<BuzzCandidate> _candidates = new List<BuzzCandidate>();
        private readonly ReadOnlyCollection<BuzzCandidate> _candidatesView;

        private double? _deadline;
        private BuzzArbiterState _state = BuzzArbiterState.Open;

        /// <summary>
        /// 受付を開始した状態で生成する。
        /// </summary>
        /// <param name="t0">受付開始時刻（サーバー時刻軸の秒）。</param>
        /// <param name="collectWindowSec">最初の押下受信からの集計窓（秒）。既定 0.15（<c>buzz.collectWindowMs</c>）。</param>
        /// <param name="penalizedClientIds">お手つきペナルティ中で受付対象外のクライアント ID（null 可）。</param>
        /// <param name="tieEpsilonSec">
        /// 同着とみなす閾値（秒）。ユーザー設定項目ではなく、テストから境界を動かすためのシーム。
        /// 通常は既定値 <see cref="DefaultTieEpsilonSec"/> のまま使う。0 以下は指定できない。
        /// </param>
        public BuzzArbiter(
            double t0,
            double collectWindowSec,
            IEnumerable<ulong> penalizedClientIds = null,
            double tieEpsilonSec = DefaultTieEpsilonSec)
        {
            if (!double.IsFinite(t0))
            {
                throw new ArgumentOutOfRangeException(nameof(t0), t0, "t0 は有限の値である必要があります。");
            }

            if (!double.IsFinite(collectWindowSec) || collectWindowSec < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(collectWindowSec), collectWindowSec, "collectWindowSec は 0 以上の有限の値である必要があります。");
            }

            if (!double.IsFinite(tieEpsilonSec) || tieEpsilonSec <= 0.0)
            {
                // 0 以下だと「差 0 の完全同着」すら抽選にならず、候補リストの先頭が常に勝つ不公平になる。
                throw new ArgumentOutOfRangeException(
                    nameof(tieEpsilonSec), tieEpsilonSec, "tieEpsilonSec は 0 より大きい有限の値である必要があります。");
            }

            _t0 = t0;
            _collectWindowSec = collectWindowSec;
            _tieEpsilonSec = tieEpsilonSec;
            _penalized = penalizedClientIds == null
                ? new HashSet<ulong>()
                : new HashSet<ulong>(penalizedClientIds);
            _candidatesView = new ReadOnlyCollection<BuzzCandidate>(_candidates);
        }

        /// <summary>受付開始時刻（サーバー時刻軸の秒）。</summary>
        public double T0 => _t0;

        /// <summary>集計窓の長さ（秒）。</summary>
        public double CollectWindowSec => _collectWindowSec;

        /// <summary>同着とみなす閾値（秒）。</summary>
        public double TieEpsilonSec => _tieEpsilonSec;

        /// <summary>現在の受付状態。</summary>
        public BuzzArbiterState State => _state;

        /// <summary>集計窓が閉じるサーバー時刻。まだ 1 件も受理していなければ null。</summary>
        public double? DeadlineServerTime => _deadline;

        /// <summary>受理済みの候補の読み取り専用ビュー（ログ・司会画面用）。</summary>
        public ReadOnlyCollection<BuzzCandidate> Candidates => _candidatesView;

        /// <summary>
        /// 押下を検証して候補に積む（docs/network.md §6.3 / §6.4）。
        /// </summary>
        /// <param name="clientId">送信元クライアント ID（RPC の SenderClientId から取ること）。</param>
        /// <param name="reportedTime">クライアントが報告した押下時刻（<c>LocalTime.Time</c>、サーバー時刻軸の秒）。</param>
        /// <param name="serverNow">サーバーが受信した時刻（サーバー時刻軸の秒）。</param>
        /// <param name="reason">棄却理由。受理時は <see cref="BuzzReject.None"/>。</param>
        /// <returns>受理したら true。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="serverNow"/> が有限でないとき。</exception>
        public bool Accept(ulong clientId, double reportedTime, double serverNow, out BuzzReject reason)
        {
            if (!double.IsFinite(serverNow))
            {
                // serverNow はサーバー自身の時刻なので、異常値は呼び出し側のバグ。握りつぶさず通知する。
                throw new ArgumentOutOfRangeException(
                    nameof(serverNow), serverNow, "serverNow は有限の値である必要があります。");
            }

            if (_state != BuzzArbiterState.Open)
            {
                reason = BuzzReject.NotOpen;
                return false;
            }

            if (_deadline != null && serverNow >= _deadline.Value)
            {
                // 締め切り後に届いた押下は、まだ TryResolve を呼んでいなくても候補にしない。
                reason = BuzzReject.WindowClosed;
                return false;
            }

            if (ContainsClient(clientId))
            {
                reason = BuzzReject.Duplicate;
                return false;
            }

            if (_penalized.Contains(clientId))
            {
                reason = BuzzReject.Penalized;
                return false;
            }

            if (!double.IsFinite(reportedTime))
            {
                reason = BuzzReject.NonFiniteTimestamp;
                return false;
            }

            // --- タイムスタンプの検証と補正（§6.4） ---
            if (reportedTime < _t0 - MaxPastSec)
            {
                // 人間の反応時間より小さい dt を名乗って単独勝者になるのを防ぐ。
                reason = BuzzReject.TooFarInPast;
                return false;
            }

            var effectiveTime = reportedTime;
            var clamp = BuzzClamp.None;

            if (effectiveTime < _t0)
            {
                // 受付直前のわずかなずれ（tick 粒度の範囲）→ T0 に丸める。
                effectiveTime = _t0;
                clamp = BuzzClamp.ToT0;
            }

            if (effectiveTime > serverNow + MaxFutureSec)
            {
                reason = BuzzReject.TooFarInFuture;
                return false;
            }

            if (effectiveTime > serverNow)
            {
                // 受信時刻より後 → 受信時刻に丸める（送信が遅れた分は不利に扱ってよい）。
                effectiveTime = serverNow;
                clamp = BuzzClamp.ToServerNow;
            }

            var dt = effectiveTime - _t0;
            if (dt < 0.0)
            {
                // serverNow < T0（受付開始より前にサーバーが受信）という異常系の保険。結果は T0 丸めと同じ。
                dt = 0.0;
                clamp = BuzzClamp.ToT0;
            }

            _candidates.Add(new BuzzCandidate(clientId, dt, clamp));

            if (_deadline == null)
            {
                // 最初の 1 件で集計窓を開始する（§6.3）。
                _deadline = serverNow + _collectWindowSec;
            }

            reason = BuzzReject.None;
            return true;
        }

        /// <summary>
        /// 集計窓が閉じていれば勝者を確定する（docs/network.md §6.3）。
        /// 窓が閉じるまでは false を返し、状態は変えない。
        /// </summary>
        /// <param name="serverNow">現在のサーバー時刻（秒）。</param>
        /// <param name="rng">同着時の抽選に使う乱数源。</param>
        /// <param name="resolution">確定した裁定結果。未解決のときは null。</param>
        /// <returns>勝者が確定したら true。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rng"/> が null のとき。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="serverNow"/> が有限でないとき。</exception>
        /// <exception cref="InvalidOperationException"><paramref name="rng"/> が範囲外の値を返したとき。</exception>
        public bool TryResolve(double serverNow, IRandom rng, out BuzzResolution? resolution)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            if (!double.IsFinite(serverNow))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(serverNow), serverNow, "serverNow は有限の値である必要があります。");
            }

            resolution = null;

            if (_state != BuzzArbiterState.Open)
            {
                return false;
            }

            if (_deadline == null || serverNow < _deadline.Value)
            {
                return false;
            }

            var best = double.MaxValue;
            for (var i = 0; i < _candidates.Count; i++)
            {
                if (_candidates[i].Dt < best)
                {
                    best = _candidates[i].Dt;
                }
            }

            var tied = new List<BuzzCandidate>();
            for (var i = 0; i < _candidates.Count; i++)
            {
                if (_candidates[i].Dt - best < _tieEpsilonSec)
                {
                    tied.Add(_candidates[i]);
                }
            }

            if (tied.Count == 0)
            {
                // _deadline が非 null なら候補は 1 件以上あるため、ここには到達しない。
                throw new InvalidOperationException("集計窓が開いているのに候補が 1 件もありません。");
            }

            BuzzCandidate winner;
            if (tied.Count == 1)
            {
                winner = tied[0];
            }
            else
            {
                var index = rng.NextInt(tied.Count);
                if (index < 0 || index >= tied.Count)
                {
                    throw new InvalidOperationException(
                        "IRandom.NextInt が範囲外の値を返しました: " + index + "（0〜" + (tied.Count - 1) + " を期待）。");
                }

                winner = tied[index];
            }

            _state = BuzzArbiterState.Resolved;
            resolution = new BuzzResolution(
                winner.ClientId,
                winner.Dt,
                tied.Count > 1,
                tied.Count,
                winner.Clamp,
                _candidates.Count,
                BuildRanking(winner.ClientId, best, tied.Count > 1));
            return true;
        }

        /// <summary>
        /// 集計窓の候補を順位順に並べる（#194、参加者パネルの押下順）。
        /// 先頭は勝者（同着の抽選で選ばれた候補を含む）、以降は Dt の昇順で、同じ Dt なら受理した順。
        /// </summary>
        /// <param name="winnerClientId">勝者のクライアント ID。</param>
        /// <param name="bestDt">最小の Dt（同着判定の基準）。</param>
        /// <param name="wasTie">抽選だったか。</param>
        /// <returns>順位順の候補。</returns>
        private List<BuzzRankEntry> BuildRanking(ulong winnerClientId, double bestDt, bool wasTie)
        {
            var others = new List<BuzzCandidate>(_candidates.Count);
            BuzzCandidate? winner = null;
            for (var i = 0; i < _candidates.Count; i++)
            {
                if (_candidates[i].ClientId == winnerClientId && winner == null)
                {
                    winner = _candidates[i];
                    continue;
                }

                others.Add(_candidates[i]);
            }

            // List.Sort は安定ではないため、受理順（元の添字）を第 2 キーにして並びを決定的にする。
            var order = new List<int>(others.Count);
            for (var i = 0; i < others.Count; i++)
            {
                order.Add(i);
            }

            order.Sort((a, b) =>
            {
                var byDt = others[a].Dt.CompareTo(others[b].Dt);
                return byDt != 0 ? byDt : a.CompareTo(b);
            });

            var ranking = new List<BuzzRankEntry>(_candidates.Count);
            if (winner.HasValue)
            {
                ranking.Add(new BuzzRankEntry(winner.Value.ClientId, winner.Value.Dt, wasTie));
            }

            for (var i = 0; i < order.Count; i++)
            {
                var candidate = others[order[i]];
                var tiedWithWinner = wasTie && candidate.Dt - bestDt < _tieEpsilonSec;
                ranking.Add(new BuzzRankEntry(candidate.ClientId, candidate.Dt, tiedWithWinner));
            }

            return ranking;
        }

        /// <summary>
        /// 勝者を決めずに受付を閉じる（誰も押さないままタイムアウトした場合など）。
        /// 既に確定・終了しているときは何もしない。
        /// </summary>
        public void Close()
        {
            if (_state == BuzzArbiterState.Open)
            {
                _state = BuzzArbiterState.Closed;
            }
        }

        /// <summary>
        /// 一時停止中に経過した秒数だけ集計窓の締め切りを後ろへずらす
        /// （司会の一時停止 #20、<see cref="QuizStateMachine.Resume"/> から呼ばれる）。
        /// 締め切りがまだ確定していない（1 件も受理していない）場合は何もしない
        /// （締め切りは次の押下受理時に <c>serverNow</c> 基準で新しく設定されるため）。
        /// </summary>
        /// <param name="deltaSec">一時停止していた秒数（0 以下なら何もしない）。</param>
        public void ShiftDeadline(double deltaSec)
        {
            if (_deadline.HasValue && deltaSec > 0.0)
            {
                _deadline += deltaSec;
            }
        }

        /// <summary>
        /// 受付中の押下候補とペナルティ集合のクライアント ID を付け替える（#84、再接続の引き継ぎ）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 早押し受付が開いている最中に切断 → 再接続したクライアントは、NGO から新しい
        /// <c>clientId</c> を割り当てられる（NGO 2.13.2 は ID を使い回さない）。この受付を裁定する
        /// <see cref="BuzzArbiter"/> はお手つきペナルティ中のクライアント ID を
        /// 生成時のスナップショットで持っているため、付け替えないと
        /// <b>切断・再接続でペナルティから逃れられてしまう</b>。
        /// </para>
        /// <para>
        /// 既に押下済みの候補も同じ人のものとして付け替える（＝再接続しても押し直せない）。
        /// 付け替え先が既に候補に居る場合（通常は起こらない）は重複させず、移動元を取り除く。
        /// </para>
        /// </remarks>
        /// <param name="fromClientId">切断時に使っていた古いクライアント ID。</param>
        /// <param name="toClientId">復帰後の新しいクライアント ID。</param>
        /// <returns>付け替えるものがあったら true。</returns>
        public bool TransferClientId(ulong fromClientId, ulong toClientId)
        {
            if (fromClientId == toClientId)
            {
                return false;
            }

            var changed = false;

            if (_penalized.Remove(fromClientId))
            {
                _penalized.Add(toClientId);
                changed = true;
            }

            for (var i = _candidates.Count - 1; i >= 0; i--)
            {
                if (_candidates[i].ClientId != fromClientId)
                {
                    continue;
                }

                if (ContainsClient(toClientId))
                {
                    _candidates.RemoveAt(i);
                }
                else
                {
                    _candidates[i] = new BuzzCandidate(toClientId, _candidates[i].Dt, _candidates[i].Clamp);
                }

                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// 受付中の押下候補とペナルティ集合から指定クライアントを取り除く（#84）。
        /// <see cref="TransferClientId"/> の裏返しで、席が名簿から消えた
        /// （保持期間切れ・ホストの手動削除）クライアントの掃除に使う。
        /// </summary>
        /// <remarks>
        /// 候補が 1 件も残らなくなったら、集計窓の締め切り（<see cref="DeadlineServerTime"/>）も
        /// <c>null</c> に戻して「まだ誰も押していない」状態にする。
        /// このクラスは<b>「締め切りが決まっている ⇒ 候補が 1 件以上ある」</b>を前提にしており
        /// （<see cref="TryResolve"/> はこれが崩れると <see cref="InvalidOperationException"/> を投げる）、
        /// 締め切りを残したままだと <c>Accept</c> も <see cref="BuzzReject.WindowClosed"/> で拒否し続けて
        /// 受付が固まるため（PR #98 レビュー H-2）。
        /// </remarks>
        /// <param name="clientId">取り除くクライアント ID。</param>
        /// <returns>取り除くものがあったら true。</returns>
        public bool ForgetClient(ulong clientId)
        {
            var changed = _penalized.Remove(clientId);

            for (var i = _candidates.Count - 1; i >= 0; i--)
            {
                if (_candidates[i].ClientId != clientId)
                {
                    continue;
                }

                _candidates.RemoveAt(i);
                changed = true;
            }

            if (_candidates.Count == 0)
            {
                // 押下が 1 件も無い状態へ戻す。以後は通常どおり buzz.timeLimitSec で時間切れになり、
                // 残っている参加者の押下も受理できる。
                _deadline = null;
            }

            return changed;
        }

        private bool ContainsClient(ulong clientId)
        {
            for (var i = 0; i < _candidates.Count; i++)
            {
                if (_candidates[i].ClientId == clientId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
