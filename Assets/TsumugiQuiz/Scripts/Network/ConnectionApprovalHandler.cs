using System;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// NGO の <c>ConnectionApprovalCallback</c> と、純 C# の判定ロジック
    /// （<see cref="ConnectionApprovalEvaluator"/>）をつなぐ薄い層。
    /// 判定そのものは Core 側にあるので EditMode でテストできる（docs/network.md §2.3 / §10.1）。
    ///
    /// 「形式・サイズ」「プロトコルバージョン一致」「ビルドの一致（#204）」「プレイヤー名」までは
    /// <see cref="ConnectionApprovalEvaluator"/>（Core）が判定する。
    /// 名簿の状態に依存する「人数上限」「フェーズ（途中参加）」「再接続」は
    /// <see cref="AdmissionEvaluator"/> 経由で <see cref="LobbyState"/> が判定する（#7）。
    /// </summary>
    public sealed class ConnectionApprovalHandler : IDisposable
    {
        /// <summary>拒否ログを残す最短間隔（秒）。同一理由の連打でログが膨れないよう間引く（#52）。</summary>
        private const double RejectLogIntervalSec = 1.0;

        private readonly NetworkManager _networkManager;

        /// <summary>
        /// 拒否ログの間引き。<b>拒否理由（<see cref="ConnectionRejectionReason"/>）単位</b>でグローバルに間引く。
        /// NGO は接続試行のたびに新しい <c>ClientNetworkId</c> を払い出すため、クライアント ID 単位の間引きは
        /// 同一の攻撃者が接続し直すたびにリセットされてしまい機能しない（#52 レビュー H1）。
        /// 拒否理由は有限個の enum なので、キーが際限なく増えることもない。
        /// </summary>
        private readonly RejectLogThrottle _rejectLogThrottle = new RejectLogThrottle(RejectLogIntervalSec);

        private bool _registered;

        /// <summary>
        /// ハンドラを生成する。
        /// </summary>
        /// <param name="networkManager">対象の <see cref="NetworkManager"/>。</param>
        /// <param name="policy">判定条件。</param>
        /// <exception cref="ArgumentNullException"><paramref name="networkManager"/> が null。</exception>
        public ConnectionApprovalHandler(NetworkManager networkManager, ConnectionApprovalPolicy policy)
        {
            _networkManager = networkManager != null
                ? networkManager
                : throw new ArgumentNullException(nameof(networkManager));
            Policy = policy;
        }

        /// <summary>現在の判定条件。</summary>
        public ConnectionApprovalPolicy Policy { get; private set; }

        /// <summary>
        /// ロビー名簿に基づく追加判定（人数上限・フェーズ・再接続。docs/network.md §2.3 の 4〜6）。
        /// 引数はクライアント ID・検証済みのプレイヤー名・再接続トークンをまとめた
        /// <see cref="LobbyAdmissionRequest"/>。null なら追加判定を行わない
        /// （ロビーがまだ立っていない場合など）。
        ///
        /// この判定は <see cref="ConnectionApprovalEvaluator"/> が承認した後にだけ呼ばれる。
        /// 実装するのは <see cref="LobbyState"/>（#7）で、承認したクライアントを
        /// 「接続完了待ち」として予約し、定員の二重取りを防ぐ。
        /// </summary>
        public Func<LobbyAdmissionRequest, LobbyAdmission> AdmissionEvaluator { get; set; }

        /// <summary>
        /// 判定を行うたびに発火する（クライアント ID と判定結果）。ログ・UI 用。
        ///
        /// <see cref="ConnectionApprovalDecision.Payload"/> は **クライアントが送ってきた内容**であり、
        /// 承認された場合でも「形式・長さ・文字種が規則を満たしている」ことしか保証しない
        /// （名乗った名前が本人のものである保証はない）。表示や突き合わせに使うときは
        /// あくまで untrusted な入力として扱い、サーバー側の権威データを別に持つこと。
        /// 拒否された場合の Payload は復号できたところまでの内容（復号自体に失敗したら既定値）。
        /// </summary>
        public event Action<ulong, ConnectionApprovalDecision> Evaluated;

        /// <summary>
        /// 判定条件を差し替える。ロビーでルーム設定が確定したときに呼ぶ想定。
        /// </summary>
        public void SetPolicy(ConnectionApprovalPolicy policy) => Policy = policy;

        /// <summary>
        /// <c>NetworkConfig.ConnectionApproval</c> を有効にし、コールバックを登録する。
        /// <c>StartHost()</c> より前に呼ぶこと（NGO は開始時に整合性を検査する）。
        /// </summary>
        public void Register()
        {
            if (_registered)
            {
                return;
            }

            _networkManager.NetworkConfig.ConnectionApproval = true;
            _networkManager.ConnectionApprovalCallback = OnConnectionApproval;
            _registered = true;
        }

        /// <summary>コールバックの登録を解除する。</summary>
        public void Unregister()
        {
            if (!_registered)
            {
                return;
            }

            // NGO の setter は「複数のデリゲートを束ねた値」だけを拒否する（単一なら上書き可）。
            // 誰が登録したかを追跡できないので、自分が登録したときだけ null に戻す。
            _networkManager.ConnectionApprovalCallback = null;
            _registered = false;
        }

        /// <inheritdoc />
        public void Dispose() => Unregister();

        /// <summary>
        /// 承認コールバック本体。クライアントから届く <c>request.Payload</c> は信用せず、すべて検証する。
        /// </summary>
        private void OnConnectionApproval(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            // プレイヤーオブジェクトは M1 では生成しない。
            // プレイヤーの状態はロビーの NetworkList / NetworkVariable で持つ方針（docs/network.md §1.2）。
            response.CreatePlayerObject = false;
            response.Pending = false;

            // ホスト自身（NetworkManager.ServerClientId == 0）は StartHost() の中から
            // このコールバックを通るが、NGO 側で「ホストの接続は拒否できない」と扱われ
            // 拒否しても警告を出して承認されてしまう。よって明示的に承認する。
            if (request.ClientNetworkId == NetworkManager.ServerClientId)
            {
                response.Approved = true;
                response.Reason = string.Empty;
                Evaluated?.Invoke(request.ClientNetworkId, ConnectionApprovalDecision.Approve(
                    new ConnectionPayload(Policy.ExpectedProtocolVersion, string.Empty, Policy.ExpectedClientBuildHash)));
                return;
            }

            // ホストモードでは ConnectedClientsIds にホスト自身（ID 0）も含まれる。
            // 司会専用モード（仮決め K18）を含む正確な定員判定は名簿を持つ LobbyState 側
            // （AdmissionEvaluator）が行うため、Policy.MaxPlayers は「名簿が無いときの粗い保険」として扱う。
            var connectedPlayerCount = _networkManager.ConnectedClientsIds.Count;
            var decision = ConnectionApprovalEvaluator.Evaluate(request.Payload, connectedPlayerCount, Policy);

            // 名簿に依存する判定（人数上限・フェーズ・再接続）は、ここまでの検証を通ったあとにだけ行う。
            // 検証前のペイロードから作った名前で名簿を引かないこと（docs/network.md §9: 受信データは信用しない）。
            if (decision.Approved && AdmissionEvaluator != null)
            {
                var admission = AdmissionEvaluator(
                    LobbyAdmissionRequest.FromPayload(request.ClientNetworkId, decision.Payload));
                if (!admission.IsApproved)
                {
                    decision = ConnectionApprovalDecision.Reject(
                        admission.RejectionReason,
                        admission.Message,
                        decision.Payload);
                }
            }

            response.Approved = decision.Approved;
            response.Reason = decision.Approved ? string.Empty : decision.ReasonMessage;

            if (!decision.Approved)
            {
                // 拒否理由ごとにグローバルに間引く（clientId は接続試行のたびに変わるため使えない、H1）。
                var logDecision = _rejectLogThrottle.Evaluate((ulong)decision.Reason, _networkManager.ServerTime.Time);
                if (logDecision.ShouldLog)
                {
                    // ログにはクライアント ID と理由だけを残す（IP は残さない: docs/network.md §9）。
                    // ビルドの不一致（#204）は、拒否の文言と同じビルドの番号（識別子そのものではない）も残す。
                    // サマリの書式は RpcRejectLogger / RpcRateGuard と共通（#83、FormatSuppressedSuffix）。
                    Debug.LogWarning(
                        "[ConnectionApprovalHandler] 接続を拒否しました "
                        + $"clientId={request.ClientNetworkId} reason={decision.Reason}{FormatBuildNumbers(decision)}"
                        + logDecision.FormatSuppressedSuffix());
                }
            }

            Evaluated?.Invoke(request.ClientNetworkId, decision);
        }

        /// <summary>
        /// ビルドの不一致で拒否したときだけ、ホストとクライアントのビルドの番号をログ用に返す（#204）。
        /// 番号はクライアントに送った拒否の文言と同じもの（<see cref="BuildDisplayNumber.ForMismatch"/>）。それ以外の理由では空。
        /// </summary>
        private string FormatBuildNumbers(ConnectionApprovalDecision decision)
        {
            if (decision.Reason != ConnectionRejectionReason.ClientBuildMismatch)
            {
                return string.Empty;
            }

            var numbers = BuildDisplayNumber.ForMismatch(Policy.ExpectedClientBuildHash, decision.Payload.ClientBuildHash);
            return $" hostBuildNumber={numbers.Host} clientBuildNumber={numbers.Client}";
        }
    }
}
