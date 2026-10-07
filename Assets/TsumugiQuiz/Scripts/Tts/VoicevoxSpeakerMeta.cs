using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// voicevox_voice_model_file_create_metas_json / voicevox_synthesizer_create_metas_json が返す
    /// 話者メタ情報（docs/tts.md §4.1）。JSON のキーは Rust の Serde 実装に準じたスネークケース。
    /// </summary>
    public sealed class VoicevoxSpeakerMeta
    {
        [JsonProperty("name")]
        public string Name { get; private set; }

        [JsonProperty("speaker_uuid")]
        public string SpeakerUuid { get; private set; }

        [JsonProperty("styles")]
        public IReadOnlyList<VoicevoxStyleMeta> Styles { get; private set; } = Array.Empty<VoicevoxStyleMeta>();

        [JsonProperty("version")]
        public string Version { get; private set; }
    }

    /// <summary>話者のスタイル。<see cref="Id"/> は VVM のバージョンで変わりうるのでハードコードしない。</summary>
    public sealed class VoicevoxStyleMeta
    {
        [JsonProperty("name")]
        public string Name { get; private set; }

        [JsonProperty("id")]
        public uint Id { get; private set; }

        /// <summary>"talk" / "singing_teacher" / "frame_decode" / "sing" など。未指定なら talk 扱い。</summary>
        [JsonProperty("type")]
        public string Type { get; private set; }
    }
}
