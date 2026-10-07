using System;
using System.Collections.Generic;
using System.Threading;
using TsumugiQuiz.Core;
using UnityEngine;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// アプリに同梱された利用規約リソース（Assets/TsumugiQuiz/Resources/Terms/*.txt）の一覧と、
    /// それぞれを Resources から読み込んでハッシュ化する処理をまとめる（requirements.md FR-71・FR-76）。
    /// <see cref="Views.TermsView"/>（本文表示）と <see cref="ConsentGate"/>（同意済み判定）の両方から使う。
    /// </summary>
    public static class TermsCatalog
    {
        /// <summary>規約の出典ページへのリンク（見出しと URL の組）。1件の規約に複数の出典がある場合がある。</summary>
        public sealed class SourceLink
        {
            public string Label { get; }
            public string Url { get; }

            public SourceLink(string label, string url)
            {
                Label = label;
                Url = url;
            }
        }

        /// <summary>1件の規約に関する表示用メタデータ。</summary>
        public sealed class Entry
        {
            /// <summary>規約 ID（<see cref="TermsDefinition.TermsId"/> と対応、ConsentRecord にもそのまま記録される）。</summary>
            public string TermsId { get; }

            /// <summary>画面に表示する見出し。</summary>
            public string DisplayName { get; }

            /// <summary>Resources.Load で読み込む際のパス（拡張子なし。Resources/ からの相対パス）。</summary>
            public string ResourcePath { get; }

            /// <summary>規約の出典 URL 一覧。「規約ページを開く」ボタン群の遷移先。0件ならボタンを表示しない。</summary>
            public IReadOnlyList<SourceLink> SourceLinks { get; }

            public Entry(string termsId, string displayName, string resourcePath, IReadOnlyList<SourceLink> sourceLinks)
            {
                TermsId = termsId;
                DisplayName = displayName;
                ResourcePath = resourcePath;
                SourceLinks = sourceLinks ?? Array.Empty<SourceLink>();
            }
        }

        /// <summary>
        /// requirements.md FR-71 が列挙する4件の規約（docs/licenses.md §12 の同意フロー節と対応）。
        /// 追加・削除する場合は Resources/Terms/*.txt の追加・削除とセットで行うこと。
        /// </summary>
        public static readonly IReadOnlyList<Entry> Entries = new[]
        {
            new Entry(
                "voicevox-models-terms",
                "VOICEVOX 音声モデル 利用規約",
                "Terms/voicevox-models-terms",
                new[] { new SourceLink("規約ページを開く", "https://github.com/VOICEVOX/voicevox_vvm") }),
            new Entry(
                "voicevox-onnxruntime-terms",
                "VOICEVOX ONNX Runtime 利用規約",
                "Terms/voicevox-onnxruntime-terms",
                new[] { new SourceLink("配布元プロジェクトを開く", "https://github.com/VOICEVOX/voicevox_core") }),
            new Entry(
                "tsumugi-voice-credit",
                "春日部つむぎ 音声規約",
                "Terms/tsumugi-voice-credit",
                new[] { new SourceLink("規約ページを開く", "https://tsumugi-official.studio.site/rule2") }),
            new Entry(
                "tsumugi-illustration-terms",
                "春日部つむぎ 立ち絵規約",
                "Terms/tsumugi-illustration-terms",
                new[]
                {
                    // docs/licenses.md §3・requirements.md FR-71 のとおり、立ち絵規約は運営の2つの
                    // ページ（rule2 / rule）双方に同様の規約が掲載されているため、両方を提示する（H-2）。
                    new SourceLink("規約ページを開く（rule2）", "https://tsumugi-official.studio.site/rule2"),
                    new SourceLink("VOICEVOX が案内する規約ページ（rule）", "https://tsumugi-official.studio.site/rule"),
                }),
        };

        // #97: 同梱リソース（Resources/Terms/*.txt）は実行中に内容が変わらないため、Resources.Load の
        // 結果をエントリ単位でプロセス内キャッシュする（M-3: LoadText と LoadRequiredTerms の両方が
        // このキャッシュを経由するので、表示テキストとハッシュが必ず同じ読み込み結果から計算される）。
        //
        // このキャッシュは、あくまで「同じ内容を毎回 Resources.Load しない」ための最適化であり、
        // #97 の本質的な修正（TtsStatusPanel の「再試行」ハンドラの継続を明示的にメインスレッドへ
        // 戻すこと）とは独立している。メインスレッド以外から初めて呼ばれた場合は、これまで通り
        // Resources.Load の「Load can only be called from the main thread」で失敗する。
        private static readonly object CacheGate = new object();

        // #97 L-4: キーは Entry 参照ではなく ResourcePath（string）にする。Entries は静的な
        // 固定配列で常に同じ参照が使われるため実害はなかったが、キャッシュの同一性を
        // 「読み込み元のリソースパス」という値で表したほうが意図が明確なため。
        private static readonly Dictionary<string, string> CachedTextByResourcePath = new Dictionary<string, string>();

        // Lazy<T>（ExecutionAndPublication）にすることで、初回呼び出しが同時に複数スレッドから
        // 来ても計算は1回だけになり（M-2）、かつ初回の計算が例外を投げた場合は失敗をキャッシュせず
        // 次回呼び出しで再試行される（M-4。.NET の Lazy<T> の仕様どおり）。
        private static Lazy<IReadOnlyList<TermsDefinition>> _requiredTermsLazy = CreateRequiredTermsLazy();

        /// <summary>
        /// <paramref name="entry"/> のテキスト本文を読み込む。結果はエントリ単位でキャッシュする（#97）ため、
        /// 2 回目以降は <c>Resources.Load</c> を呼ばない。
        /// 見つからない場合は例外を投げる（同梱必須のリソースが欠けているのはアプリの構成不備のため、
        /// 握りつぶさずに fail-fast する。失敗はキャッシュしないので、リソース配置後の再呼び出しで直る）。
        /// </summary>
        public static string LoadText(Entry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            lock (CacheGate)
            {
                if (CachedTextByResourcePath.TryGetValue(entry.ResourcePath, out var cachedText))
                {
                    return cachedText;
                }
            }

            var asset = Resources.Load<TextAsset>(entry.ResourcePath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"[TermsCatalog] 規約テキストが見つかりません: Resources/{entry.ResourcePath}.txt");
            }

            var text = asset.text;
            lock (CacheGate)
            {
                CachedTextByResourcePath[entry.ResourcePath] = text;
            }

            return text;
        }

        /// <summary>
        /// <see cref="Entries"/> すべてを読み込み、現在のテキスト本文（ヘッダーを除く）から計算した
        /// SHA-256 を含む <see cref="TermsDefinition"/> の一覧を返す。<see cref="ConsentStore.HasAcceptedAll"/> /
        /// <see cref="ConsentStore.RecordConsent"/> にそのまま渡せる。
        ///
        /// 結果はプロセス内でキャッシュする（#97）。テキスト自体はビルドに同梱された固定リソースであり、
        /// 実行中に変化しないため、2 回目以降は <see cref="Resources.Load{T}(string)"/> を呼ばない。
        /// </summary>
        public static IReadOnlyList<TermsDefinition> LoadRequiredTerms() => _requiredTermsLazy.Value;

        private static IReadOnlyList<TermsDefinition> ComputeRequiredTerms()
        {
            var result = new List<TermsDefinition>(Entries.Count);
            foreach (var entry in Entries)
            {
                var text = LoadText(entry);
                var hash = TermsHasher.ComputeSha256HexForTermsBody(text);
                result.Add(new TermsDefinition(entry.TermsId, hash));
            }

            return result.AsReadOnly();
        }

        private static Lazy<IReadOnlyList<TermsDefinition>> CreateRequiredTermsLazy()
            => new Lazy<IReadOnlyList<TermsDefinition>>(ComputeRequiredTerms, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// テスト専用（#97 L-2/L-3）。<see cref="LoadText"/> / <see cref="LoadRequiredTerms"/> の
        /// キャッシュを両方とも破棄する。「初回はメインスレッドで済ませ、以降は別スレッドから呼んでも
        /// Resources.Load に到達しない」という構造を、キャッシュが冷えた状態から明示的に検証したい
        /// テストのために用意している。通常のコードパスから呼ぶ必要はない。
        /// </summary>
        internal static void ResetCacheForTesting()
        {
            lock (CacheGate)
            {
                CachedTextByResourcePath.Clear();
            }

            _requiredTermsLazy = CreateRequiredTermsLazy();
        }
    }
}
