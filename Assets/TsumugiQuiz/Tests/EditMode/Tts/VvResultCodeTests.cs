using NUnit.Framework;
using TsumugiQuiz.Tts.Native;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>結果コード → 例外の変換（docs/tts-native-api.md §2.2）の検証。</summary>
    public sealed class VvResultCodeTests
    {
        [Test]
        public void Check_Okなら例外を投げない()
        {
            Assert.DoesNotThrow(() => Vv.Check(VoicevoxResultCode.Ok, "テスト"));
        }

        [TestCase(VoicevoxResultCode.NotLoadedOpenjtalkDict, 1)]
        [TestCase(VoicevoxResultCode.RunModel, 8)]
        [TestCase(VoicevoxResultCode.InvalidUtf8Input, 12)]
        [TestCase(VoicevoxResultCode.InitInferenceRuntime, 29)]
        public void Check_Ok以外はResultCode付きの例外になる(VoicevoxResultCode code, int expected)
        {
            var e = Assert.Throws<VoicevoxException>(() => Vv.Check(code, "音声合成"));

            Assert.That(e.ResultCode, Is.EqualTo(expected), "結果コードの数値を保持すること");
            Assert.That(e.Message, Does.Contain("音声合成"), "何に失敗したかを含むこと");
            Assert.That(e.Message, Does.Contain(expected.ToString()), "ログ用に結果コードを含むこと");
            Assert.That(e.NativeMessage, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void DescribeResultCode_DLLが無くても列挙子名にフォールバックして例外にならない()
        {
            // 実 DLL がある環境ではネイティブのメッセージ、無ければ列挙子名が返る。
            var message = Vv.DescribeResultCode(VoicevoxResultCode.StyleNotFound);

            Assert.That(message, Is.Not.Null.And.Not.Empty);
        }
    }
}
