using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.Questions;
using TsumugiQuiz.UI.Views.HostSetup;

namespace TsumugiQuiz.Tests.EditMode.UI.HostSetup
{
    /// <summary>
    /// <see cref="HostSetupPresenter"/> の EditMode テスト。
    /// <c>HostAddressInfo</c> を（実ネットワークに触れず）組み立てて渡し、
    /// 参加コード表示用の整形（インターネット / LAN の 2 種、公開 IP 未取得時の代替文言）と、
    /// 案内文の選択ロジック（手動ポート開放案内・手入力欄・CGNAT 警告の出し分け）を検証する。
    /// </summary>
    public class HostSetupPresenterTests
    {
        [Test]
        public void Build_NotResolved_ReturnsPlaceholderState()
        {
            var state = HostSetupPresenter.Build(HostAddressInfo.NotResolved);

            Assert.IsFalse(state.IsResolved);
            Assert.IsFalse(state.CanCopyInternetCode);
            Assert.IsFalse(state.CanCopyLanCode);
            Assert.IsFalse(state.ShowManualGuide);
            Assert.IsFalse(state.ShowManualIpInput);
            Assert.IsEmpty(state.Warnings);
            Assert.IsEmpty(state.ManualGuideRows);
        }

        [Test]
        public void Build_FullSuccess_FormatsBothJoinCodes_UsingKnownTestVectors()
        {
            // docs/network-joincode.md §1.7 のテストベクタ。
            var portMapping = PortMappingResult.Ok(7777, 7777, "203.0.113.5", "UPnP");
            var publicIp = PublicIpResult.Resolved("203.0.113.5", PublicIpSource.NatDevice, "203.0.113.5", "203.0.113.5", "ok");
            var info = HostAddressInfo.Create(portMapping, publicIp, "192.168.1.23", 7777);

            var state = HostSetupPresenter.Build(info);

            Assert.IsTrue(state.IsResolved);
            Assert.AreEqual("6B01-RGA7-K1K4", state.InternetCodeText);
            Assert.IsTrue(state.CanCopyInternetCode);
            Assert.AreEqual("60N0-0HE7-K12R", state.LanCodeText);
            Assert.IsTrue(state.CanCopyLanCode);
            Assert.IsFalse(state.ShowManualGuide);
            Assert.IsFalse(state.ShowManualIpInput);
            Assert.IsEmpty(state.Warnings);
            StringAssert.Contains("成功", state.PortMappingStatusText);
            StringAssert.Contains("UPnP", state.PortMappingStatusText);
            Assert.AreEqual("ルーター（UPnP / NAT-PMP）から取得", state.PublicIpSourceText);
        }

        [Test]
        public void Build_CarrierGradeNat_HidesInternetCode_ButKeepsLanCode_AndRequestsManualIpInput()
        {
            var portMapping = PortMappingResult.Ok(7777, 7777, "100.64.0.1", "UPnP");
            var publicIp = PublicIpResult.Resolved("100.64.0.1", PublicIpSource.NatDevice, "100.64.0.1", "100.64.0.1", "ok");
            var info = HostAddressInfo.Create(portMapping, publicIp, "192.168.1.23", 7777);

            var state = HostSetupPresenter.Build(info);

            Assert.IsFalse(state.CanCopyInternetCode);
            StringAssert.Contains("CGNAT", state.InternetCodeText);
            Assert.AreEqual("60N0-0HE7-K12R", state.LanCodeText, "LAN 用コードは CGNAT の影響を受けないはず。");
            Assert.IsTrue(state.CanCopyLanCode);
            Assert.IsTrue(state.ShowManualIpInput);
            Assert.IsFalse(state.ShowManualGuide, "ポート開放自体は成功しているので手動ポート開放案内は不要。");
            Assert.IsTrue(HasWarningContaining(state, "CGNAT"));
        }

        [Test]
        public void Build_PortMappingFailed_ShowsManualGuide_WithFourRows()
        {
            var portMapping = PortMappingResult.Fail(PortMappingStatus.DeviceNotFound, 7777, "UPnP デバイスが見つかりませんでした。");
            var publicIp = PublicIpResult.Resolved("203.0.113.5", PublicIpSource.IpLookupService, string.Empty, "203.0.113.5", "ok");
            var info = HostAddressInfo.Create(portMapping, publicIp, "192.168.1.23", 7777);

            var state = HostSetupPresenter.Build(info);

            // ルーターの自動開放には失敗しているが、グローバル IP 自体は取得できているので
            // 参加コードは（外部から実際に届くかはさておき）内部ポートを使って作成できる。
            Assert.IsTrue(state.CanCopyInternetCode);
            Assert.AreEqual("6B01-RGA7-K1K4", state.InternetCodeText);
            // H-1: IP 自体は取得できていても、自動ポート開放に失敗している場合は
            // （手動でポートを開けないと実際には届かない可能性があるため）手入力欄も出す。
            Assert.IsTrue(state.ShowManualIpInput);
            Assert.IsTrue(state.ShowManualGuide);
            Assert.AreEqual(4, state.ManualGuideRows.Count);
            Assert.AreEqual("外部ポート", state.ManualGuideRows[1].Key);
            Assert.AreEqual("7777", state.ManualGuideRows[1].Value);
            Assert.AreEqual("宛先 IP", state.ManualGuideRows[3].Key);
            Assert.AreEqual("192.168.1.23", state.ManualGuideRows[3].Value);
            StringAssert.Contains("IP 確認サービスから取得", state.PublicIpSourceText);
        }

        [Test]
        public void Build_NoPublicIpAtAll_ShowsFallbackMessage_AndRequiresManualInput()
        {
            var portMapping = PortMappingResult.Fail(PortMappingStatus.Timeout, 7777, "タイムアウトしました。");
            var publicIp = PublicIpResult.NotFound(string.Empty, "グローバル IP を取得できませんでした。");
            var info = HostAddressInfo.Create(portMapping, publicIp, "192.168.1.5", 7777);

            var state = HostSetupPresenter.Build(info);

            Assert.IsFalse(state.CanCopyInternetCode);
            StringAssert.Contains("グローバル IP が未取得", state.InternetCodeText);
            Assert.IsTrue(state.ShowManualIpInput);
            Assert.IsTrue(state.ShowManualGuide);
            Assert.IsTrue(state.CanCopyLanCode, "LAN 用コードはグローバル IP が無くても作成できるはず。");
            Assert.AreEqual("未取得", state.PublicIpSourceText);
        }

        [Test]
        public void Build_ManualPublicIpApplied_OverridesCarrierGradeNat_AndCreatesInternetCode()
        {
            var portMapping = PortMappingResult.Fail(PortMappingStatus.DeviceNotFound, 7777, "見つかりませんでした。");
            var publicIp = PublicIpResult.NotFound(string.Empty, "取得できませんでした。");
            var info = HostAddressInfo.Create(portMapping, publicIp, "192.168.1.5", 7777)
                .WithManualPublicIpAddress("100.65.1.2"); // Tailscale 相当の CGNAT 帯アドレス。

            var state = HostSetupPresenter.Build(info);

            Assert.IsTrue(state.CanCopyInternetCode);
            Assert.AreEqual(TsumugiQuiz.Core.JoinCodeCodec.Encode("100.65.1.2", 7777), state.InternetCodeText);
            Assert.AreEqual("手入力（100.65.1.2）", state.PublicIpSourceText);
            Assert.IsFalse(HasWarningContaining(state, "CGNAT"), "手入力アドレスには CGNAT 判定を適用しない。");
        }

        [Test]
        public void Build_LanIpMissing_ShowsFallbackMessage_ForLanCodeOnly()
        {
            var portMapping = PortMappingResult.Ok(7777, 7777, "203.0.113.5", "UPnP");
            var publicIp = PublicIpResult.Resolved("203.0.113.5", PublicIpSource.NatDevice, "203.0.113.5", "203.0.113.5", "ok");
            var info = HostAddressInfo.Create(portMapping, publicIp, string.Empty, 7777);

            var state = HostSetupPresenter.Build(info);

            Assert.IsTrue(state.CanCopyInternetCode);
            Assert.IsFalse(state.CanCopyLanCode);
            StringAssert.Contains("LAN", state.LanCodeText);
        }

        [Test]
        public void BuildQuestionIssueLines_NoIssues_ReturnsEmpty()
        {
            var report = new QuestionLoadReport(
                DateTimeOffset.Now,
                Array.Empty<QuestionSet>(),
                Array.Empty<QuestionSetLoadError>(),
                Array.Empty<string>());

            var lines = HostSetupPresenter.BuildQuestionIssueLines(report);

            Assert.IsEmpty(lines);
        }

        [Test]
        public void BuildQuestionIssueLines_SkippedSets_FormatsFileNameAndEachMessage()
        {
            var skipped = new[]
            {
                new QuestionSetLoadError(
                    @"C:\questions\broken.json",
                    new[] { "JSON の形式が不正です。", "setId が重複しています。" }),
            };

            var report = new QuestionLoadReport(
                DateTimeOffset.Now,
                Array.Empty<QuestionSet>(),
                skipped,
                Array.Empty<string>());

            var lines = HostSetupPresenter.BuildQuestionIssueLines(report);

            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual("broken.json: JSON の形式が不正です。", lines[0]);
            Assert.AreEqual("broken.json: setId が重複しています。", lines[1]);
        }

        [Test]
        public void BuildQuestionIssueLines_DuplicateFolderErrors_AreCollapsedToOneLine()
        {
            const string duplicateMessage = "問題フォルダのファイル数が上限を超えています。";
            var report = new QuestionLoadReport(
                DateTimeOffset.Now,
                Array.Empty<QuestionSet>(),
                Array.Empty<QuestionSetLoadError>(),
                new[] { duplicateMessage, duplicateMessage });

            var lines = HostSetupPresenter.BuildQuestionIssueLines(report);

            Assert.AreEqual(1, lines.Count, "統括メモ: FolderErrors の重複は1行にまとめてよい。");
            Assert.AreEqual(duplicateMessage, lines[0]);
        }

        [Test]
        public void BuildQuestionIssueLines_NullReport_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => HostSetupPresenter.BuildQuestionIssueLines(null));
        }

        private static bool HasWarningContaining(HostSetupUiState state, string fragment)
        {
            foreach (var warning in state.Warnings)
            {
                if (warning.Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
