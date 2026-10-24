using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.UI;
using UnityEngine.Video;

namespace RuneCode
{
    /// <summary>
    /// 도움말 항목의 페이지 본문·영상·이미지와 좌우 페이지 버튼을 표시한다. 목차가 있는 도움말 창과 첫 등장 안내 창이 함께 쓴다.
    /// 닫기 동작을 받으면 마지막 페이지의 다음 버튼이 닫기 버튼이 된다.
    /// </summary>
    public sealed class EncyclopediaPageView : MonoBehaviour
    {
        private const string EMPTY_BODY = "표시할 도움말이 없습니다. 잠시 후 다시 확인해 주세요.";
        private const string NEXT_LABEL = "다음";
        private const string CLOSE_LABEL = "닫기";

        [Header("본문")]
        [SerializeField] private ScrollRect _bodyScroll;
        [SerializeField] private TextMeshProUGUI _entryBody;

        [Header("페이지")]
        [SerializeField] private Button _prevPageButton;
        [SerializeField] private Button _nextPageButton;
        [SerializeField] private TextMeshProUGUI _nextPageLabel;
        [SerializeField] private TextMeshProUGUI _pageLabel;

        private IReadOnlyList<EncyclopediaPage> _pages;
        private Action _closeRequested;
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

        void Awake()
        {
            _mediaAspect = _videoImage.GetComponent<AspectRatioFitter>();
            _prevPageButton.onClick.AddListener(() => ShowPage(_pageIndex - 1));
            _nextPageButton.onClick.AddListener(HandleNext);
        }

        void OnDisable()
        {
            StopVideo();
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
        /// pages의 첫 페이지를 표시한다. closeRequested가 있으면 마지막 페이지의 다음 버튼을 닫기로 바꾸고 누르면 호출하며,
        /// 없으면 마지막 페이지에서 다음 버튼을 끈다. pages가 비어 있으면 빈 본문 한 페이지로 표시한다.
        /// </summary>
        public void Show(IReadOnlyList<EncyclopediaPage> pages, Action closeRequested)
        {
            _pages = pages;
            _closeRequested = closeRequested;
            ShowPage(0);
        }

        /// <summary>마지막 페이지에서 닫기 동작이 있으면 호출하고, 아니면 다음 페이지로 넘긴다.</summary>
        private void HandleNext()
        {
            int pageCount = _pages?.Count ?? 0;
            if (_closeRequested != null && _pageIndex >= pageCount - 1) _closeRequested();
            else ShowPage(_pageIndex + 1);
        }

        /// <summary>pageIndex의 본문·영상·이미지와 페이지 버튼 상태를 갱신하고 본문 스크롤을 맨 위로 올린다.</summary>
        private void ShowPage(int pageIndex)
        {
            int pageCount = _pages?.Count ?? 0;
            _pageIndex = pageCount > 0 ? Mathf.Clamp(pageIndex, 0, pageCount - 1) : 0;
            EncyclopediaPage page = pageCount > 0 ? _pages[_pageIndex] : null;
            _entryBody.text = page?.Body ?? EMPTY_BODY;

            bool isSinglePage = pageCount <= 1;
            bool isLastPage = _pageIndex >= pageCount - 1;
            bool isClosable = _closeRequested != null;
            _prevPageButton.gameObject.SetActive(!isSinglePage);
            _pageLabel.gameObject.SetActive(!isSinglePage);
            _nextPageButton.gameObject.SetActive(!isSinglePage || isClosable);
            _prevPageButton.interactable = _pageIndex > 0;
            _nextPageButton.interactable = !isLastPage || isClosable;
            _nextPageLabel.text = isLastPage && isClosable ? CLOSE_LABEL : NEXT_LABEL;
            if (!isSinglePage) _pageLabel.text = (_pageIndex + 1) + " / " + pageCount;

            ShowMedia(page);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_bodyScroll.content);
            Canvas.ForceUpdateCanvases();
            _bodyScroll.StopMovement();
            _bodyScroll.verticalNormalizedPosition = 1;
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
                if (hasImage) Debug.LogWarning("EncyclopediaPageView: 한 페이지에 영상과 이미지가 함께 지정되어 영상만 표시합니다. image=" + imagePath);
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
                Debug.LogWarning("EncyclopediaPageView: 이미지를 로드할 수 없습니다. path=" + imagePath);
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
                Debug.LogWarning("EncyclopediaPageView: 영상을 로드할 수 없습니다. path=" + videoPath);
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

        /// <summary>영상 출력용 RenderTexture를 처음 필요할 때 한 번 만들어 VideoPlayer에 연결한 뒤 보관한다.</summary>
        private RenderTexture GetVideoTexture()
        {
            if (_videoTexture != null) return _videoTexture;
            _videoTexture = new RenderTexture(_videoTextureSize.x, _videoTextureSize.y, 0);
            _videoTexture.name = "EncyclopediaVideoTexture";
            _videoPlayer.targetTexture = _videoTexture;
            return _videoTexture;
        }
    }
}
