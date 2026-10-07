using System;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// NAT デバイス操作の失敗。Mono.Nat の <c>MappingException</c> などをこの型に翻訳して投げる。
    /// </summary>
    public sealed class NatDeviceException : Exception
    {
        /// <summary>
        /// 例外を作る。
        /// </summary>
        /// <param name="kind">失敗種別。</param>
        /// <param name="message">ログ用のメッセージ。</param>
        /// <param name="innerException">元の例外。</param>
        public NatDeviceException(NatFailureKind kind, string message, Exception innerException = null)
            : base(message, innerException)
        {
            Kind = kind;
        }

        /// <summary>失敗種別。</summary>
        public NatFailureKind Kind { get; }
    }
}
