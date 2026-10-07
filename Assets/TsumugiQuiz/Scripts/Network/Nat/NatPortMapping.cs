using System;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// ポートマッピング 1 件（不変）。Mono.Nat の <c>Mapping</c> を層の外に漏らさないための値型。
    /// プロトコルは常に UDP（Unity Transport が UDP を使うため、docs/network-nat.md §1.3）。
    /// </summary>
    public readonly struct NatPortMapping : IEquatable<NatPortMapping>
    {
        // default(NatPortMapping) でも null 参照にならないよう、文字列はプロパティ側で畳む（H-2）。
        private readonly string _description;

        /// <summary>
        /// マッピングを作る。
        /// </summary>
        /// <param name="privatePort">内部ポート（このPCが待ち受けているポート）。</param>
        /// <param name="publicPort">外部ポート。</param>
        /// <param name="lifetimeSeconds">有効期間（秒）。0 は無期限。</param>
        /// <param name="description">ルーターに記録される説明。</param>
        public NatPortMapping(int privatePort, int publicPort, int lifetimeSeconds, string description)
        {
            PrivatePort = privatePort;
            PublicPort = publicPort;
            LifetimeSeconds = lifetimeSeconds;
            _description = description;
        }

        /// <summary>内部ポート。</summary>
        public int PrivatePort { get; }

        /// <summary>
        /// 外部ポート。要求した値が埋まっていた場合にルーターが別の値を返すことがあるため、
        /// 参加コードには **作成後に返ってきた値** を使う（docs/network-nat.md §1.3）。
        /// </summary>
        public int PublicPort { get; }

        /// <summary>有効期間（秒）。0 は無期限。</summary>
        public int LifetimeSeconds { get; }

        /// <summary>説明。本アプリのマッピングは <see cref="NatOptions.MappingDescription"/>。</summary>
        public string Description => _description ?? string.Empty;

        /// <summary>ポート番号が <c>ushort</c> の範囲に収まっているか（ルーターの応答を信用しない）。</summary>
        public bool HasValidPublicPort => PublicPort > 0 && PublicPort <= ushort.MaxValue;

        /// <inheritdoc />
        public bool Equals(NatPortMapping other)
            => PrivatePort == other.PrivatePort
               && PublicPort == other.PublicPort
               && LifetimeSeconds == other.LifetimeSeconds
               && Description == other.Description;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is NatPortMapping other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = PrivatePort;
                hash = (hash * 397) ^ PublicPort;
                hash = (hash * 397) ^ LifetimeSeconds;
                hash = (hash * 397) ^ (Description != null ? Description.GetHashCode() : 0);
                return hash;
            }
        }

        /// <inheritdoc />
        public override string ToString()
            => $"UDP {PrivatePort} -> {PublicPort} (lifetime={LifetimeSeconds}s, description={Description})";
    }
}
