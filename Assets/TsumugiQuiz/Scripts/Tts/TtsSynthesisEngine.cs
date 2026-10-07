using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// <see cref="VoicevoxSynthesizer"/> にスタイル解決とキューイングを足した合成エンジン。
    ///
    /// <list type="bullet">
    ///   <item><description>合成キューは<b>同時実行 1</b>（<see cref="SemaphoreSlim"/>）。
    ///     voicevox_core の合成は CPU を大量に使うため、並列化するとゲーム本体が重くなる（docs/tts.md §7.4）</description></item>
    ///   <item><description>合成は必ずワーカースレッドで走る（<see cref="VoicevoxSynthesizer"/> の契約）</description></item>
    ///   <item><description>速度 1.0 は一括合成、それ以外は AudioQuery の <c>speedScale</c> 書き換え
    ///     → <c>synthesis</c> の 2 段系統（docs/tts.md §6.4）</description></item>
    /// </list>
    ///
    /// <see cref="Dispose"/> は実行中の合成が終わるまでブロックする（<see cref="VoicevoxSynthesizer"/> の仕様）。
    /// </summary>
    internal sealed class TtsSynthesisEngine : ITtsSynthesisEngine
    {
        /// <summary>AudioQuery の速度フィールド名。</summary>
        private const string SpeedScaleField = "speedScale";

        /// <summary>モデルのバージョンが取れないときにキャッシュキーへ入れる値。</summary>
        private const string UnknownModelsVersion = "unknown";

        private readonly VoicevoxSynthesizer _synthesizer;
        private readonly SemaphoreSlim _queue = new SemaphoreSlim(1, 1);
        private bool _disposed;

        private TtsSynthesisEngine(
            VoicevoxSynthesizer synthesizer, VoicevoxStyleResolution style, string coreVersion, string modelsVersion)
        {
            _synthesizer = synthesizer;
            Style = style;
            CoreVersion = coreVersion;
            ModelsVersion = modelsVersion;
        }

        /// <summary>解決されたスタイル（docs/tts.md §4.1 のフォールバック順を適用済み）。</summary>
        public VoicevoxStyleResolution Style { get; }

        /// <summary>解決された話者名（キャッシュキーに入る）。</summary>
        public string SpeakerName => Style.SpeakerName;

        /// <summary>解決されたスタイル名（キャッシュキーに入る）。</summary>
        public string StyleName => Style.StyleName;

        /// <summary><c>voicevox_get_version()</c>（キャッシュキーに入る）。</summary>
        public string CoreVersion { get; }

        /// <summary>読み込んだ音声モデルのバージョン（キャッシュキーに入る）。</summary>
        public string ModelsVersion { get; }

        /// <summary>読み込んだ .vvm のパス一覧（診断ログ用）。</summary>
        public IReadOnlyList<string> LoadedModelFiles => _synthesizer.LoadedModelFiles;

        /// <summary>
        /// 初期化してスタイルを解決する。<b>数秒かかるのでメインスレッドから呼ばないこと</b>。
        /// </summary>
        /// <exception cref="TtsSetupException">配置不足・スタイル解決失敗</exception>
        /// <exception cref="Native.VoicevoxException">ネイティブ側の失敗</exception>
        public static TtsSynthesisEngine Create(VoicevoxLocation location, TtsSettings settings)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var synthesizer = VoicevoxSynthesizer.Create(location, VoicevoxModelSelection.FromSettings(settings));
            try
            {
                var metasJson = synthesizer.CreateMetasJson();
                var metas = VoicevoxStyleResolver.ParseMetas(metasJson);
                var style = VoicevoxStyleResolver.Resolve(metas, settings.SpeakerName, settings.StyleName);

                return new TtsSynthesisEngine(
                    synthesizer, style, synthesizer.CoreVersion, DescribeModelsVersion(metas, style));
            }
            catch
            {
                synthesizer.Dispose();
                throw;
            }
        }

        /// <summary>
        /// テキストを合成して wav バイト列を返す。キューが空くまで待つ。
        /// </summary>
        /// <exception cref="OperationCanceledException">キャンセルされたとき</exception>
        /// <exception cref="Native.VoicevoxException">合成が失敗したとき</exception>
        public async Task<byte[]> SynthesizeAsync(string text, float speed, CancellationToken cancellationToken)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            // Dispose 済みなら SemaphoreSlim に触れる前に弾く。
            // （破棄と同時に走っている合成が ObjectDisposedException を投げないよう、
            //   SemaphoreSlim 自体は Dispose しない。GC に任せてよい）
            if (_disposed) throw new ObjectDisposedException(nameof(TtsSynthesisEngine));

            await _queue.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 合成は必ずメインスレッド外で行う（VoicevoxSynthesizer の契約）。
                return await Task.Run(() => SynthesizeCore(text, speed), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _queue.Release();
            }
        }

        /// <summary>
        /// 破棄する。実行中の合成が終わるまでブロックする
        /// （<see cref="VoicevoxSynthesizer.Dispose"/> の仕様）。2 回目以降の呼び出しは何もしない。
        ///
        /// <b><see cref="_queue"/>（<see cref="SemaphoreSlim"/>）は Dispose しない。</b>
        /// 破棄と同時に <see cref="SynthesizeAsync"/> が走っていると
        /// <see cref="ObjectDisposedException"/> が飛び、読み上げの失敗がログに残ってしまうため。
        /// <see cref="SemaphoreSlim"/> はアンマネージリソースを握らない（<c>AvailableWaitHandle</c> を
        /// 使っていない場合）ので、GC に任せて問題ない。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _synthesizer.Dispose();
        }

        /// <summary>ログ用の説明文。</summary>
        public string Describe()
            => $"style={SpeakerName}/{StyleName} id={Style.StyleId} match={Style.Match} " +
               $"core={CoreVersion} models={ModelsVersion} vvm={LoadedModelFiles.Count}";

        private byte[] SynthesizeCore(string text, float speed)
        {
            if (TtsSpeed.IsDefault(speed))
            {
                return _synthesizer.Tts(text, Style.StyleId);
            }

            // VoicevoxTtsOptions に速度パラメータが無いので、AudioQuery の speedScale を書き換える。
            var queryJson = _synthesizer.CreateAudioQuery(text, Style.StyleId);
            var query = JObject.Parse(queryJson);

            // speedScale 以外のフィールドは触らない。
            query[SpeedScaleField] = speed;

            return _synthesizer.Synthesis(query.ToString(Newtonsoft.Json.Formatting.None), Style.StyleId);
        }

        /// <summary>
        /// モデルのバージョン文字列を作る。voicevox_core / 音声モデルを更新すると
        /// 同じ入力でも波形が変わるため、キャッシュキーに含める（docs/tts.md §7.1）。
        ///
        /// <b>実際に使う話者の version だけを見る。</b>
        /// 全話者を連結すると、読み上げに関係のない別の .vvm が増減しただけで
        /// キャッシュが丸ごと無効になってしまうため。
        /// </summary>
        private static string DescribeModelsVersion(
            IReadOnlyList<VoicevoxSpeakerMeta> metas, VoicevoxStyleResolution style)
        {
            var version = metas
                .Where(m => m != null && string.Equals(m.Name, style.SpeakerName, StringComparison.Ordinal))
                .Select(m => m.Version)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            return string.IsNullOrWhiteSpace(version) ? UnknownModelsVersion : version;
        }
    }
}
