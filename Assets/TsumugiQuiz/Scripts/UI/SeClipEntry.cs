using System;
using UnityEngine;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// <see cref="SeKind"/> と <see cref="AudioClip"/> の組。<see cref="SePlayer"/> の
    /// Inspector 上でのクリップ割り当てに使う。
    /// </summary>
    [Serializable]
    public sealed class SeClipEntry
    {
        [SerializeField]
        private SeKind _kind;

        [SerializeField]
        private AudioClip _clip;

        public SeClipEntry()
        {
        }

        public SeClipEntry(SeKind kind, AudioClip clip)
        {
            _kind = kind;
            _clip = clip;
        }

        public SeKind Kind => _kind;

        public AudioClip Clip => _clip;
    }
}
