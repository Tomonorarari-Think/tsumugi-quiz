using System;
using System.Collections.Generic;
using System.Linq;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// <see cref="SeKind"/> と、scripts/gen-se.py が生成する wav ファイル名・
    /// Unity プロジェクト内のアセットパスとの対応表。
    /// Unity API に依存しない純 C# なので、EditMode/PlayMode テストや将来のエディタ拡張
    /// （SePlayer への自動アサイン等）の両方から参照できる。
    /// </summary>
    public static class SeAssetPaths
    {
        /// <summary>Assets/TsumugiQuiz/Audio/SE/ の相対パス（プロジェクトルート基準）。</summary>
        public const string FolderPath = "Assets/TsumugiQuiz/Audio/SE";

        private static readonly IReadOnlyDictionary<SeKind, string> FileNames = new Dictionary<SeKind, string>
        {
            [SeKind.Buzz] = "buzz.wav",
            [SeKind.Correct] = "correct.wav",
            [SeKind.Wrong] = "wrong.wav",
            [SeKind.TimeUp] = "timeup.wav",
            [SeKind.Start] = "start.wav",
            [SeKind.Join] = "join.wav",
        };

        /// <summary>
        /// 定義済みのすべての <see cref="SeKind"/>。<c>Enum.GetValues</c> から導出するため、
        /// enum に値を追加した場合は（<see cref="FileNames"/> の対応漏れがない限り）自動的に反映される。
        /// 対応漏れは <c>SeAssetPathsCompletenessTests</c>（EditMode）で検出する。
        /// </summary>
        public static IReadOnlyCollection<SeKind> AllKinds { get; } =
            ((SeKind[])Enum.GetValues(typeof(SeKind))).ToList().AsReadOnly();

        /// <summary>指定した <see cref="SeKind"/> に対応する wav ファイル名（例: "buzz.wav"）。</summary>
        public static string GetFileName(SeKind kind)
        {
            if (!FileNames.TryGetValue(kind, out var fileName))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の SeKind です。");
            }

            return fileName;
        }

        /// <summary>
        /// 指定した <see cref="SeKind"/> に対応するアセットパス（例: "Assets/TsumugiQuiz/Audio/SE/buzz.wav"）。
        /// <c>AssetDatabase.LoadAssetAtPath</c> にそのまま渡せる形式。
        /// </summary>
        public static string GetAssetPath(SeKind kind) => $"{FolderPath}/{GetFileName(kind)}";
    }
}
