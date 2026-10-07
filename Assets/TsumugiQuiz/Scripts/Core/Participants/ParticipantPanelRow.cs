namespace TsumugiQuiz.Core.Participants
{
    /// <summary>参加者パネル（#194）の 1 行ぶんの表示内容。不変。文言への変換は UI 層が行う。</summary>
    public readonly struct ParticipantPanelRow
    {
        /// <summary>値を指定して生成する。</summary>
        public ParticipantPanelRow(
            ulong clientId,
            string name,
            bool isLocal,
            bool isConnected,
            ParticipantStatus status,
            int buzzRank,
            bool tiedWithWinner,
            int answerOrder,
            int score)
        {
            ClientId = clientId;
            Name = name ?? string.Empty;
            IsLocal = isLocal;
            IsConnected = isConnected;
            Status = status;
            BuzzRank = buzzRank;
            TiedWithWinner = tiedWithWinner;
            AnswerOrder = answerOrder;
            Score = score;
        }

        /// <summary>クライアント ID。</summary>
        public ulong ClientId { get; }

        /// <summary>表示名。</summary>
        public string Name { get; }

        /// <summary>自分の行か。</summary>
        public bool IsLocal { get; }

        /// <summary>接続中か（切断中はグレー表示）。</summary>
        public bool IsConnected { get; }

        /// <summary>現在の問題での状態。</summary>
        public ParticipantStatus Status { get; }

        /// <summary>直近の早押しでの押下順位（1 = 勝者）。出さないときは 0。</summary>
        public int BuzzRank { get; }

        /// <summary>勝者と同着の抽選だったか。</summary>
        public bool TiedWithWinner { get; }

        /// <summary>
        /// 回答権を得た順番（1 人目, 2 人目, …）。この問題で回答権を得た人が 2 人以上いるときだけ 1 以上、それ以外は 0
        /// （1 人しかいないなら順番を出す意味が無いため）。
        /// </summary>
        public int AnswerOrder { get; }

        /// <summary>累計得点（得点を出さないときも値は入るが、表示するかは <see cref="ParticipantPanelState.ShowScores"/> で決める）。</summary>
        public int Score { get; }
    }
}
