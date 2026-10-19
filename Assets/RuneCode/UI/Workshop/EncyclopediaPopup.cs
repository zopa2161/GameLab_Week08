using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>전달받은 게임 도움말의 목차와 설명을 독립적으로 스크롤하는 공용 팝업이다. 페이지 본문·영상·이미지는 EncyclopediaPageView가 표시한다.</summary>
    public sealed class EncyclopediaPopup : UiPopup
    {
        private const string EMPTY_TITLE = "도움말 준비 중";
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
        [SerializeField] private TextMeshProUGUI _entryTitle;
        [SerializeField] private EncyclopediaPageView _pageView;

        [Header("창 바깥 닫기")]
        [SerializeField] private Button _backgroundButton;

        void Awake()
        {
            _backgroundButton.onClick.AddListener(Close);
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
                _pageView.Show(null, null);
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

        /// <summary>index에 해당하는 제목·페이지·목차 선택 표시를 갱신한다. 개방되지 않은 항목은 무시한다.</summary>
        private void SelectEntry(int index)
        {
            if (_data?.Entries == null || index < 0 || index >= _data.Entries.Count || index >= _unlockedCount) return;
            if (_selectedIndex >= 0 && _selectedIndex < _tabs.Count) _tabs[_selectedIndex].SetSelected(false);
            _selectedIndex = index;
            _tabs[index].SetSelected(true);
            EncyclopediaEntry entry = _data.Entries[index];
            _entryTitle.text = entry?.Title ?? EMPTY_TITLE;
            _pageView.Show(entry?.Pages, null);
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
