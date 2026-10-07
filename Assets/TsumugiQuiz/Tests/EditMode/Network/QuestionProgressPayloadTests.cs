using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core.Participants;
using TsumugiQuiz.Network;
using Unity.Collections;
using Unity.Netcode;

namespace TsumugiQuiz.Tests.EditMode.Network
{
    /// <summary>
    /// 現在の問題の進行状態の同期ペイロード（<see cref="QuestionProgressPayload"/>、#194）の
    /// 往復・サイズ・上限・受信時の検証を確かめる。
    /// </summary>
    public sealed class QuestionProgressPayloadTests
    {
        private static byte[] Serialize(QuestionProgressPayload payload)
        {
            using var writer = new FastBufferWriter(4096, Allocator.Temp);
            writer.WriteNetworkSerializable(payload);
            return writer.ToArray();
        }

        private static QuestionProgressPayload Deserialize(byte[] bytes)
        {
            using var reader = new FastBufferReader(bytes, Allocator.Temp);
            reader.ReadNetworkSerializable(out QuestionProgressPayload payload);
            return payload;
        }

        private static QuestionProgress Progress(int questionIndex, int count)
        {
            var entries = new List<ParticipantProgress>();
            for (var i = 0; i < count; i++)
            {
                entries.Add(new ParticipantProgress(
                    (ulong)(i + 1), i + 1, i % 3, ParticipantProgressFlags.ChoiceSubmitted | ParticipantProgressFlags.TiedWithWinner));
            }

            return QuestionProgress.Create(questionIndex, entries);
        }

        [Test]
        public void Empty_HasNoQuestionAndNoEntries()
        {
            var progress = QuestionProgressPayload.Empty.ToQuestionProgress();

            Assert.AreEqual(QuestionProgress.NoQuestion, progress.QuestionIndex);
            Assert.AreEqual(0, progress.Entries.Count);
            Assert.AreNotEqual(default(QuestionProgressPayload), QuestionProgressPayload.Empty, "default は問題インデックス 0 になるので Empty と区別できること。");
        }

        [Test]
        public void RoundTrip_PreservesAllFields()
        {
            var original = QuestionProgress.Create(3, new[]
            {
                new ParticipantProgress(7, 2, 0, ParticipantProgressFlags.TiedWithWinner),
                new ParticipantProgress(2, 1, 1, ParticipantProgressFlags.None),
                new ParticipantProgress(9, 0, 0, ParticipantProgressFlags.SuspendedSkipNext),
                new ParticipantProgress(4, 0, 2, ParticipantProgressFlags.WrongAnswered | ParticipantProgressFlags.Correct),
            });

            var payload = QuestionProgressPayload.FromProgress(original, out var dropped);
            var restored = Deserialize(Serialize(payload));

            Assert.AreEqual(0, dropped);
            Assert.AreEqual(payload, restored);
            Assert.AreEqual(original, restored.ToQuestionProgress());
        }

        [Test]
        public void MaxEntries_FitsInFixedListCapacity()
        {
            var list = new FixedList512Bytes<ParticipantProgressEntry>();

            Assert.GreaterOrEqual(list.Capacity, QuestionProgressPayload.MaxEntries);
            Assert.GreaterOrEqual(QuestionProgressPayload.MaxEntries, 12, "定員（room.maxPlayers の上限 12 人）は必ず載る。");
        }

        [Test]
        public void FromProgress_OverMaxEntries_DropsTheRestAndReportsCount()
        {
            var payload = QuestionProgressPayload.FromProgress(Progress(0, QuestionProgressPayload.MaxEntries + 3), out var dropped);

            Assert.AreEqual(3, dropped);
            Assert.AreEqual(QuestionProgressPayload.MaxEntries, payload.Entries.Length);
        }

        [Test]
        public void Serialize_Size_StaysSmall()
        {
            var bytes = Serialize(QuestionProgressPayload.FromProgress(Progress(5, 12), out _));

            // 1 行 11 バイト（ClientId 8 + 順位・回答順・ビット各 1）+ 見出し 5 バイト。
            TestContext.WriteLine($"QuestionProgressPayload（12 人）の実測サイズ: {bytes.Length} バイト。");
            Assert.AreEqual(4 + 1 + (12 * 11), bytes.Length);
        }

        [Test]
        public void Deserialize_CountOverMaxEntries_ReadsAllBytesAndKeepsOnlyMaxEntries()
        {
            // PR #201 レビュー L-6: 改造・バージョン違いの送信で件数が上限を超えていても、バイト列は最後まで読み進め、
            // 上限を超えた分だけを捨てる（後続のデータの読み取り位置をずらさない）。
            const int count = QuestionProgressPayload.MaxEntries + 3;
            byte[] bytes;
            using (var writer = new FastBufferWriter(4096, Allocator.Temp))
            {
                writer.WriteValueSafe(7);            // QuestionIndex
                writer.WriteValueSafe((byte)count);  // 件数
                for (var i = 0; i < count; i++)
                {
                    writer.WriteValueSafe((ulong)(i + 1));
                    writer.WriteValueSafe((byte)1);  // BuzzRank
                    writer.WriteValueSafe((byte)0);  // AnswerOrder
                    writer.WriteValueSafe((byte)ParticipantProgressFlags.ChoiceSubmitted);
                }

                writer.WriteValueSafe(12345);        // 後続のデータ（読み取り位置がずれていないかの目印）
                bytes = writer.ToArray();
            }

            using var reader = new FastBufferReader(bytes, Allocator.Temp);
            reader.ReadNetworkSerializable(out QuestionProgressPayload payload);
            reader.ReadValueSafe(out int trailer);

            Assert.AreEqual(7, payload.QuestionIndex);
            Assert.AreEqual(QuestionProgressPayload.MaxEntries, payload.Entries.Length, "上限を超えた分は捨てる。");
            Assert.AreEqual((ulong)QuestionProgressPayload.MaxEntries, payload.Entries[QuestionProgressPayload.MaxEntries - 1].ClientId);
            Assert.AreEqual(12345, trailer, "上限を超えた分もバイト列は読み進めている。");
        }

        [Test]
        public void ToQuestionProgress_UnknownFlagBitsAndDuplicates_AreSanitized()
        {
            var payload = QuestionProgressPayload.Empty;
            payload.QuestionIndex = 1;
            payload.Entries.Add(new ParticipantProgressEntry { ClientId = 5, BuzzRank = 1, Flags = 0xE0 });
            payload.Entries.Add(new ParticipantProgressEntry { ClientId = 5, BuzzRank = 2, Flags = (byte)ParticipantProgressFlags.WrongAnswered });
            payload.Entries.Add(new ParticipantProgressEntry { ClientId = 6, Flags = 0x80 });

            var progress = Deserialize(Serialize(payload)).ToQuestionProgress();

            Assert.AreEqual(1, progress.Entries.Count, "未知のビットだけの行は空の行として捨て、同じ ID は後の行を採用する。");
            Assert.AreEqual(5UL, progress.Entries[0].ClientId);
            Assert.AreEqual(2, progress.Entries[0].BuzzRank);
            Assert.AreEqual(ParticipantProgressFlags.WrongAnswered, progress.Entries[0].Flags);
        }

        [Test]
        public void Equals_DifferentEntry_IsFalse()
        {
            var a = QuestionProgressPayload.FromProgress(Progress(0, 2), out _);
            var b = QuestionProgressPayload.FromProgress(Progress(0, 3), out _);
            var c = QuestionProgressPayload.FromProgress(Progress(1, 2), out _);

            Assert.AreEqual(a, QuestionProgressPayload.FromProgress(Progress(0, 2), out _));
            Assert.AreNotEqual(a, b);
            Assert.AreNotEqual(a, c);
        }
    }
}
