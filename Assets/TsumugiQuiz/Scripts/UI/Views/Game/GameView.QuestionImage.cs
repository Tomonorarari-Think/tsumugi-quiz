using TsumugiQuiz.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// <see cref="GameView"/> のうち、問題画像（FR-12、issue #185）の表示をまとめた部分。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 画像は <see cref="QuestionDistributor"/> がチャンクで受け取り・復号・キャッシュする（#16、
    /// docs/network.md §8.4）。ここではそのキャッシュを <see cref="QuestionDistributor.TryGetImage"/> で
    /// 参照して貼るだけで、テクスチャの寿命は配信器側に従う（<c>Destroy</c> しない）。
    /// </para>
    /// <para>
    /// 反映のきっかけは 3 つ。
    /// (1) 問題の提示（<c>HandleQuestionShown</c> / 取りこぼし復元の <c>RefreshFromCurrentState</c>）。
    /// 現在問の画像 Ack は受付開始のゲートに含まれるので、通常はこの時点で画像が揃っている。
    /// (2) 受信完了（<see cref="QuestionDistributor.ImageReceived"/>）。Ack 期限切れで先に提示された場合や、
    /// NAK からの再送で後から届いた場合に、表示中の問題の画像なら貼る。
    /// (3) 無効化（<see cref="QuestionDistributor.ImageInvalidated"/>）。差し替え・破棄が決まった画像は
    /// 次の出題で <c>Destroy</c> されるため、表示中の問題の画像なら取り直す（無ければ隠す）。
    /// 次の問題の配信（<c>QuestionDataRpc</c>）で前の問題の画像が無効化されるので、次問へ進むと必ず消える。
    /// </para>
    /// <para>
    /// 前提: <see cref="QuestionDistributor.ResetImageCache"/>（新しいセッションの現在問 0 の配信・配信器の破棄）は
    /// <see cref="QuestionDistributor.ImageInvalidated"/> を発火せずにテクスチャを <c>Destroy</c> する。
    /// そのときこの画面が破棄済みのテクスチャを貼っていても、直後の出題（<c>QuestionShown</c>）で
    /// <see cref="ShowQuestionImageFor"/> が取り直すので、表示が残り続けることはない
    /// （破棄済みのテクスチャは何も描画されない）。
    /// </para>
    /// <para>
    /// 画像エリアは中央列の上にあり（#193）、中央列の残りの高さを埋める（大きさは USS だけで決まる。
    /// <see cref="QuestionImageLayout"/>）。ここでは画像を貼って表示・非表示を切り替えるだけで、表示サイズは計算しない。
    /// 画像が現れると問題パネル以下は中央列の下側へ寄る。文字送り（#144）の途中で画像が後から届いた場合
    /// （Ack 期限切れ・NAK 再送）は、そのとき 1 回だけずれる（docs/architecture.md §10.2）。
    /// </para>
    /// <para>
    /// 画像が無い・未着・受信失敗のときは画像エリアごと非表示にし（畳む）、問題パネルを中央列の上に詰める。
    /// </para>
    /// </remarks>
    public sealed partial class GameView
    {
        private VisualElement _questionImageContainer;
        private Image _questionImage;

        /// <summary>画像イベントを購読している配信器（購読解除の相手を取り違えないため保持する）。</summary>
        private QuestionDistributor _imageDistributor;

        /// <summary>問題画像の要素を取得する（<see cref="OnShow"/> から）。見つからなくても画面は続行する。</summary>
        private void InitializeQuestionImage(VisualElement root)
        {
            _questionImageContainer = root.Q<VisualElement>("question-image-container");
            _questionImage = root.Q<Image>("question-image");

            if (_questionImageContainer == null || _questionImage == null)
            {
                Debug.LogWarning("[GameView] 問題画像の要素が見つかりません。画像なしで表示します。");
                _questionImageContainer = null;
                _questionImage = null;
                return;
            }

            _questionImage.scaleMode = ScaleMode.ScaleToFit;
            ClearQuestionImage();
        }

        /// <summary>画像のイベントを購読する（<c>SubscribeSessionEvents</c> から）。</summary>
        private void SubscribeQuestionImageEvents()
        {
            UnsubscribeQuestionImageEvents();

            _imageDistributor = _session != null ? _session.Distributor : null;
            if (_imageDistributor == null)
            {
                return;
            }

            _imageDistributor.ImageReceived += HandleQuestionImageReceived;
            _imageDistributor.ImageInvalidated += HandleQuestionImageInvalidated;
        }

        /// <summary>画像のイベント購読を外す（冪等）。</summary>
        private void UnsubscribeQuestionImageEvents()
        {
            if (_imageDistributor == null)
            {
                return;
            }

            _imageDistributor.ImageReceived -= HandleQuestionImageReceived;
            _imageDistributor.ImageInvalidated -= HandleQuestionImageInvalidated;
            _imageDistributor = null;
        }

        /// <summary>画像の表示を片付ける（<see cref="OnHide"/> から）。</summary>
        private void TeardownQuestionImage()
        {
            UnsubscribeQuestionImageEvents();
            ClearQuestionImage();

            _questionImageContainer = null;
            _questionImage = null;
        }

        /// <summary>
        /// 指定した問題の画像を表示する。手元に無ければ隠す（前の問題の画像を残さない）。
        /// </summary>
        /// <param name="questionIndex">表示中の問題インデックス。</param>
        private void ShowQuestionImageFor(int questionIndex)
        {
            var distributor = _session != null ? _session.Distributor : null;
            if (distributor != null && distributor.TryGetImage(questionIndex, out var texture))
            {
                ApplyQuestionImage(texture);
                return;
            }

            ClearQuestionImage();
        }

        private void HandleQuestionImageReceived(int questionIndex, Texture2D texture)
        {
            if (questionIndex != _displayedQuestionIndex)
            {
                // 先読み分。提示されたとき（ShowQuestionImageFor）に取り出す。
                return;
            }

            ApplyQuestionImage(texture);
        }

        private void HandleQuestionImageInvalidated(int questionIndex)
        {
            if (questionIndex != _displayedQuestionIndex)
            {
                return;
            }

            // 差し替えなら新しい画像が既に入っていることがあるので、隠す前に取り直す。
            ShowQuestionImageFor(questionIndex);
        }

        private void ApplyQuestionImage(Texture2D texture)
        {
            if (_questionImage == null || _questionImageContainer == null)
            {
                return;
            }

            // Unity の == は破棄済みのテクスチャも null と判定する（配信器が Destroy した後の参照を貼らない）。
            if (texture == null)
            {
                ClearQuestionImage();
                return;
            }

            if (!QuestionImageLayout.IsDisplayableSize(texture.width, texture.height))
            {
                Debug.LogWarning(
                    $"[GameView] 問題画像の寸法が不正なため表示しません（{texture.width}x{texture.height}）。");
                ClearQuestionImage();
                return;
            }

            _questionImage.image = texture;
            _questionImageContainer.style.display = DisplayStyle.Flex;
        }

        private void ClearQuestionImage()
        {
            if (_questionImage != null)
            {
                _questionImage.image = null;
            }

            if (_questionImageContainer != null)
            {
                _questionImageContainer.style.display = DisplayStyle.None;
            }
        }
    }
}
