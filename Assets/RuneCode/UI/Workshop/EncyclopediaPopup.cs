using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.UI;
using UnityEngine.Video;

namespace RuneCode
{
    /// <summary>전달받은 게임 도움말의 목차와 설명을 독립적으로 스크롤하는 공용 팝업이다.</summary>
    public sealed class EncyclopediaPopup : UiPopup
    {
        private const string EMPTY_TITLE = "도움말 준비 중";
        private const string EMPTY_BODY = "표시할 도움말이 없습니다. 잠시 후 다시 확인해 주세요.";
        private const string LOCKED_TITLE = "???";

        [Header("목차")]
        [SerializeField] private ScrollRect _contentsScroll;
        [SerializeField] private UiRow _rowPrefab;
        [SerializeField] private TextMeshProUGUI _entryCount;
        [SerializeField] private float _rowHeight = 54;

        private readonly List<UiRow> _rows = new List<UiRow>();
        private readonly List<TabButton> _tabs = new List<TabButton>();
        private EncyclopediaData _data;
        private int _unlockedCount;
        private int _selectedIndex = -1;

        [Header("설명")]
        [SerializeField] private ScrollRect _bodyScroll;
        [SerializeField] private TextMeshProUGUI _entryTitle;
        [SerializeField] private TextMeshProUGUI _entryBody;

        [Header("페이지")]
        [SerializeField] private Button _prevPageButton;
        [SerializeField] private Button _nextPageButton;
        [SerializeField] private TextMeshProUGUI _pageLabel;

        private int _pageIndex;

        // 영상과 이미지는 같은 영역(_videoRoot)과 RawImage(_videoImage)에 번갈아 표시한다.
        [Header("영상·이미지")]
        [SerializeField] private GameObject _videoRoot;
        [SerializeField] private RawImage _videoImage;
        [SerializeField] private VideoPlayer _videoPlayer;
        [SerializeField] private Vector2Int _videoTextureSize = new Vector2Int(640, 360);

        private RenderTexture _videoTexture;
        private string _loadedVideoPath;
        private AspectRatioFitter _mediaAspect;

        [Header("창 바깥 닫기")]
        [SerializeField] private Button _backgroundButton;

        void Awake()
        {
            _mediaAspect = _videoImage.GetComponent<AspectRatioFitter>();
            _backgroundButton.onClick.AddListener(Close);
            _prevPageButton.onClick.AddListener(() => ShowPage(_pageIndex - 1));
            _nextPageButton.onClick.AddListener(() => ShowPage(_pageIndex + 1));
        }

        void OnDisable()
        {
            _videoPlayer.Stop();
            _videoPlayer.clip = null;
            _loadedVideoPath = null;
            if (_videoTexture != null) _videoTexture.Release();
        }

        void OnDestroy()
        {
            if (_videoTexture == null) return;
            _videoTexture.Release();
            Destroy(_videoTexture);
            _videoTexture = null;
        }

        /// <summary>
        /// data와 개방 수로 목차를 구성하고 selectChapter에 해당하는 항목을 선택한다.
        /// selectChapter가 0이면 개방된 마지막 항목을, 개방 항목이 없으면 기존 EMPTY 문구를 표시한다.
        /// </summary>
        public void Configure(EncyclopediaData data, int unlockedCount, int selectChapter)
        {
            int count = data?.Entries?.Count ?? 0;
            int unlocked = Mathf.Clamp(unlockedCount, 0, count);
            if (_data != data || _unlockedCount != unlocked)
            {
                _data = data;
                _unlockedCount = unlocked;
                ConfigureRows();
            }

            _entryCount.text = "개방 " + unlocked + " / 전체 " + count + "개 항목";
            if (_selectedIndex >= 0 && _selectedIndex < _tabs.Count) _tabs[_selectedIndex].SetSelected(false);
            _selectedIndex = -1;
            if (count == 0 || unlocked == 0)
            {
                _entryTitle.text = EMPTY_TITLE;
                _pageIndex = 0;
                ShowPage(0);
                ResetScroll(_bodyScroll);
            }
            else
            {
                int index = selectChapter > 0 ? selectChapter - 1 : unlocked - 1;
                SelectEntry(Mathf.Clamp(index, 0, unlocked - 1));
            }
            ResetScroll(_contentsScroll);
        }

        /// <summary>현재 데이터의 번호·제목·클릭 동작을 목차 행에 연결하고 개방되지 않은 행은 잠금 문구와 클릭 불가로 둔다. 여분의 행은 숨긴다.</summary>
        private void ConfigureRows()
        {
            int count = _data?.Entries?.Count ?? 0;
            for (int index = 0; index < count; index++)
            {
                if (index == _rows.Count)
                {
                    UiRow row = Instantiate(_rowPrefab, _contentsScroll.content, false);
                    _rows.Add(row);
                    _tabs.Add(row.GetComponent<TabButton>());
                }
                bool isLocked = index >= _unlockedCount;
                string title = isLocked ? LOCKED_TITLE : (_data.Entries[index]?.Title ?? EMPTY_TITLE);
                int entryIndex = index;
                UiRow current = _rows[index];
                current.gameObject.SetActive(true);
                current.Configure((index + 1).ToString("00") + "   " + title, _rowHeight,
                    isLocked ? null : () => SelectEntry(entryIndex), _rowPrefab.Label.fontSize);
            }
            for (int index = count; index < _rows.Count; index++) _rows[index].gameObject.SetActive(false);
        }

        /// <summary>index에 해당하는 제목·본문·영상·목차 선택 표시를 갱신하고 오른쪽 스크롤만 초기화한다. 개방되지 않은 항목은 무시한다.</summary>
        private void SelectEntry(int index)
        {
            if (_data?.Entries == null || index < 0 || index >= _data.Entries.Count || index >= _unlockedCount) return;
            if (_selectedIndex >= 0 && _selectedIndex < _tabs.Count) _tabs[_selectedIndex].SetSelected(false);
            _selectedIndex = index;
            _tabs[index].SetSelected(true);
            _entryTitle.text = _data.Entries[index]?.Title ?? EMPTY_TITLE;
            _pageIndex = 0;
            ShowPage(0);
            ResetScroll(_bodyScroll);
        }

        /// <summary>현재 선택된 항목의 페이지 목록을 반환한다. 선택이 없거나 항목이 없으면 null을 반환한다.</summary>
        private IReadOnlyList<EncyclopediaPage> CurrentPages()
        {
            if (_selectedIndex < 0 || _data?.Entries == null || _selectedIndex >= _data.Entries.Count) return null;
            return _data.Entries[_selectedIndex]?.Pages;
        }

        /// <summary>pageIndex의 본문·영상·이미지와 페이지 버튼 상태를 갱신한다. 페이지가 없으면 빈 본문 한 페이지로 처리하고 미디어 영역을 숨긴다.</summary>
        private void ShowPage(int pageIndex)
        {
            IReadOnlyList<EncyclopediaPage> pages = CurrentPages();
            int pageCount = pages?.Count ?? 0;
            _pageIndex = pageCount > 0 ? Mathf.Clamp(pageIndex, 0, pageCount - 1) : 0;
            EncyclopediaPage page = pageCount > 0 ? pages[_pageIndex] : null;

            _entryBody.text = page?.Body ?? EMPTY_BODY;

            bool isSinglePage = pageCount <= 1;
            _prevPageButton.gameObject.SetActive(!isSinglePage);
            _nextPageButton.gameObject.SetActive(!isSinglePage);
            _pageLabel.gameObject.SetActive(!isSinglePage);
            if (!isSinglePage)
            {
                _pageLabel.text = (_pageIndex + 1) + " / " + pageCount;
                _prevPageButton.interactable = _pageIndex > 0;
                _nextPageButton.interactable = _pageIndex < pageCount - 1;
            }

            ShowMedia(page);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_videoRoot.transform.parent);
        }

        /// <summary>
        /// page의 영상 경로가 있으면 영상을, 없으면 이미지 경로의 Texture2D를 표시한다. 둘 다 있으면 경고 후 영상을 쓰고,
        /// 둘 다 없거나 이미지 로드에 실패하면 미디어 영역을 숨긴다. 이미지는 원본 비율로 맞춘다.
        /// </summary>
        private void ShowMedia(EncyclopediaPage page)
        {
            string videoPath = page?.Video;
            string imagePath = page?.Image;
            bool hasImage = !string.IsNullOrEmpty(imagePath);
            if (!string.IsNullOrEmpty(videoPath))
            {
                if (hasImage) Debug.LogWarning("EncyclopediaPopup: 한 페이지에 영상과 이미지가 함께 지정되어 영상만 표시합니다. image=" + imagePath);
                ShowVideo(videoPath);
                return;
            }
            StopVideo();
            if (!hasImage)
            {
                _videoRoot.SetActive(false);
                return;
            }
            Texture2D texture = Resources.Load<Texture2D>(imagePath);
            if (texture == null)
            {
                Debug.LogWarning("EncyclopediaPopup: 이미지를 로드할 수 없습니다. path=" + imagePath);
                _videoRoot.SetActive(false);
                return;
            }
            _videoImage.texture = texture;
            _mediaAspect.aspectRatio = (float)texture.width / texture.height;
            _videoRoot.SetActive(true);
        }

        /// <summary>videoPath의 VideoClip을 Resources에서 로드해 재생한다. 로드에 실패하면 미디어 영역을 숨기고, 직전과 같은 경로면 재생을 이어간다.</summary>
        private void ShowVideo(string videoPath)
        {
            _mediaAspect.aspectRatio = (float)_videoTextureSize.x / _videoTextureSize.y;
            if (videoPath == _loadedVideoPath && _videoPlayer.clip != null)
            {
                _videoImage.texture = _videoTexture;
                _videoRoot.SetActive(true);
                return;
            }
            VideoClip clip = Resources.Load<VideoClip>(videoPath);
            if (clip == null)
            {
                Debug.LogWarning("EncyclopediaPopup: 영상을 로드할 수 없습니다. path=" + videoPath);
                StopVideo();
                _videoRoot.SetActive(false);
                return;
            }
            _videoImage.texture = GetVideoTexture();
            _videoRoot.SetActive(true);
            _videoPlayer.clip = clip;
            _videoPlayer.Play();
            _loadedVideoPath = videoPath;
        }

        /// <summary>영상 재생을 멈추고 클립 참조와 기록한 경로를 지운다. 미디어 영역 표시는 바꾸지 않는다.</summary>
        private void StopVideo()
        {
            _videoPlayer.Stop();
            _videoPlayer.clip = null;
            _loadedVideoPath = null;
        }

        /// <summary>영상 출력용 RenderTexture를 처음 필요할 때 한 번 만들어 VideoPlayer와 RawImage에 연결한 뒤 보관한다.</summary>
        private RenderTexture GetVideoTexture()
        {
            if (_videoTexture != null) return _videoTexture;
            _videoTexture = new RenderTexture(_videoTextureSize.x, _videoTextureSize.y, 0);
            _videoTexture.name = "EncyclopediaVideoTexture";
            _videoPlayer.targetTexture = _videoTexture;
            _videoImage.texture = _videoTexture;
            return _videoTexture;
        }

        /// <summary>scroll의 변경된 콘텐츠 높이를 반영하고 관성을 멈춘 뒤 세로 위치를 맨 위로 이동한다.</summary>
        private static void ResetScroll(ScrollRect scroll)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            Canvas.ForceUpdateCanvases();
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1;
        }
    }
}
