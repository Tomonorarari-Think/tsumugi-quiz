using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// SE（効果音）の再生窓口。<see cref="AudioSource"/> 1つで <c>PlayOneShot</c> により再生する。
    /// Boot シーンに常駐させ、<see cref="Instance"/> 経由でどのシーン・View からも呼び出せるようにする
    /// （#2 NetworkBootstrap と同様に DontDestroyOnLoad で 1 インスタンスのみ維持する作法）。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class SePlayer : MonoBehaviour
    {
        /// <summary>Boot シーンに常駐する唯一のインスタンス。存在しない場合は null。</summary>
        public static SePlayer Instance { get; private set; }

        [SerializeField]
        [Tooltip("SeKind ごとの再生クリップ。SeAssetPaths の 6 種類すべてを設定すること。")]
        private List<SeClipEntry> _clips = new();

        [SerializeField]
        [Range(0f, 1f)]
        private float _volume = 1f;

        private AudioSource _audioSource;
        private AudioListener _audioListener;
        private Dictionary<SeKind, AudioClip> _clipLookup;

        /// <summary>再生音量（0〜1）。</summary>
        public float Volume
        {
            get => _volume;
            set => _volume = Mathf.Clamp01(value);
        }

        /// <summary>
        /// アタッチされている <see cref="AudioSource"/>。Awake を経由しない文脈
        /// （EditMode テストなど）でも動くよう、初回アクセス時に GetComponent で解決する。
        /// </summary>
        private AudioSource AudioSourceComponent =>
            _audioSource != null ? _audioSource : (_audioSource = GetComponent<AudioSource>());

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Boot シーンが多重にロードされた場合などに備え、後から生成された方を破棄する。
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // SePlayer には（Boot単独ロード時にAudioListenerが1つも無いケースのフォールバックとして）
            // AudioListener を同居させることがある（SePlayerBootstrap 参照）。Main シーンの
            // Camera 等、他に有効な AudioListener が存在する場合は「シーンに複数の
            // AudioListener があります」という警告を避けるため無効化する。シーン遷移のたびに
            // 状態を見直す必要があるため sceneLoaded を購読する。
            _audioListener = GetComponent<AudioListener>();
            if (_audioListener != null)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                RefreshAudioListenerState();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_audioListener != null)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RefreshAudioListenerState();
        }

        private void RefreshAudioListenerState()
        {
            if (_audioListener == null)
            {
                return;
            }

            var hasOtherActiveListener = false;
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                if (listener == _audioListener)
                {
                    continue;
                }

                if (listener.enabled && listener.gameObject.activeInHierarchy)
                {
                    hasOtherActiveListener = true;
                    break;
                }
            }

            _audioListener.enabled = !hasOtherActiveListener;
        }

        /// <summary>
        /// Inspector で設定済みの <see cref="SeClipEntry"/> 一覧をテスト・エディタ拡張から
        /// 差し替えるためのAPI。通常のゲーム実行時は Inspector 設定のみで完結する。
        /// </summary>
        public void SetClips(IEnumerable<SeClipEntry> entries)
        {
            _clips = new List<SeClipEntry>(entries);
            _clipLookup = null;
        }

        /// <summary>
        /// 指定した <see cref="SeKind"/> に対応する <see cref="AudioClip"/> を解決する。
        /// 未設定の場合は false を返す（呼び出し側で警告ログ等を出す想定）。
        /// </summary>
        public bool TryResolveClip(SeKind kind, out AudioClip clip)
        {
            EnsureLookupBuilt();
            return _clipLookup.TryGetValue(kind, out clip);
        }

        /// <summary>指定した SE を再生する。クリップが未設定の場合はエラーログを出し、再生をスキップする。</summary>
        public void Play(SeKind kind)
        {
            if (!TryResolveClip(kind, out var clip) || clip == null)
            {
                Debug.LogError($"[SePlayer] '{kind}' に対応する SE クリップが見つかりません。再生をスキップします。");
                return;
            }

            AudioSourceComponent.PlayOneShot(clip, _volume);
        }

        private void EnsureLookupBuilt()
        {
            if (_clipLookup != null)
            {
                return;
            }

            _clipLookup = new Dictionary<SeKind, AudioClip>();
            foreach (var entry in _clips)
            {
                if (entry == null || entry.Clip == null)
                {
                    continue;
                }

                _clipLookup[entry.Kind] = entry.Clip;
            }
        }
    }
}
