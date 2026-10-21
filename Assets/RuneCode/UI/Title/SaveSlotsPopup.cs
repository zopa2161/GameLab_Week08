using System;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>타이틀에서 3개 저장 슬롯을 조회·선택·생성·삭제하는 팝업이다.</summary>
    public sealed class SaveSlotsPopup : UiPopup
    {
        public enum Mode
        {
            NewGame,
            ContinueGame,
            Manage,
            SettingsLoad
        }

        private const int SLOT_COUNT = 3;
        private const float SLOT_BUTTON_X = 632f;
        private const float SLOT_BUTTON_WIDTH = 106f;

        [Header("팝업 구성")]
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private GameObject _panelPrefab;
        [SerializeField] private Sprite _panelSprite;
        [SerializeField] private GameObject _buttonPrefab;

        private readonly TextMeshProUGUI[] _slotLabels = new TextMeshProUGUI[SLOT_COUNT];
        private readonly Button[] _selectButtons = new Button[SLOT_COUNT];
        private readonly Button[] _deleteButtons = new Button[SLOT_COUNT];
        private TextMeshProUGUI _titleLabel;
        private TextMeshProUGUI _hintLabel;
        private RuneCodeSession _session;
        private UIManager _ui;
        private Action _onSlotChanged;
        private Mode _mode;
        private bool _isBuilt;

        /// <summary>세션과 UI 관리자를 연결하고 저장 슬롯 표시를 갱신한다.</summary>
        public void Configure(RuneCodeSession session, UIManager ui, Mode mode = Mode.Manage, Action onSlotChanged = null)
        {
            _session = session;
            _ui = ui;
            _mode = mode;
            _onSlotChanged = onSlotChanged;
            BuildIfNeeded();
            Refresh();
        }

        /// <summary>새로 하기 또는 이어 하기에서 선택 가능한 슬롯이 있는지 반환하며 손상 파일은 빈 슬롯으로 취급하지 않는다.</summary>
        public static bool HasEligibleSlot(Mode mode)
        {
            for (int slot = 1; slot <= SLOT_COUNT; slot++)
            {
                if (mode == Mode.NewGame && !SaveStore.HasSlot(slot)) return true;
                if (mode == Mode.ContinueGame && SaveStore.TryLoadSlot(slot, out _)) return true;
            }
            return false;
        }

        /// <summary>사용 모드와 저장 파일 유효성에 맞춰 제목, 슬롯 문구와 선택·삭제 버튼을 갱신한다.</summary>
        private void Refresh()
        {
            string titleKey = _mode == Mode.NewGame ? "ui.newGame" : _mode == Mode.ContinueGame ? "ui.continueGame"
                : _mode == Mode.SettingsLoad ? "save.loadSlot" : "save.manage";
            string hintKey = _mode == Mode.NewGame ? "save.newGameHint" : _mode == Mode.ContinueGame ? "save.continueHint"
                : _mode == Mode.SettingsLoad ? "save.loadSlotHint" : "save.manageHint";
            _titleLabel.text = GameData.L(titleKey);
            _hintLabel.text = GameData.L(hintKey);
            for (int index = 0; index < SLOT_COUNT; index++)
            {
                int slot = index + 1;
                bool hasFile = SaveStore.HasSlot(slot);
                bool hasSave = SaveStore.TryLoadSlot(slot, out PlayerSave save);
                string slotName = GameData.L("save.slot") + " " + slot;
                if (hasSave)
                {
                    _slotLabels[index].text = slotName + " · " + FormatProgress(save)
                        + " · " + GameData.L("ui.fragments") + " " + save.Currency;
                }
                else
                {
                    _slotLabels[index].text = slotName + " · " + GameData.L(hasFile ? "save.slotReadError" : "save.slotEmpty");
                }
                bool canSelect = _mode == Mode.NewGame ? !hasFile
                    : (_mode == Mode.ContinueGame || _mode == Mode.SettingsLoad) && hasSave;
                _selectButtons[index].gameObject.SetActive(canSelect);
                _selectButtons[index].interactable = canSelect;
                SetButtonLabel(_selectButtons[index], GameData.L(_mode == Mode.NewGame ? "save.select"
                    : _mode == Mode.SettingsLoad ? "save.load" : "ui.continueGame"));
                _deleteButtons[index].gameObject.SetActive(_mode == Mode.Manage && hasFile);
                _deleteButtons[index].interactable = hasFile;
            }
        }

        /// <summary>저장된 연속 미션 진행(마지막으로 끝낸 스테이지·라운드와 그 시점 체력)을 슬롯 문구로 반환하고 기록이 없으면 새 진행 문구를 반환한다.</summary>
        private static string FormatProgress(PlayerSave save)
        {
            if (!save.HasProgressionRound) return GameData.L("save.progressNone");
            return string.Format(GameData.L("save.progressRound"), save.ProgressionStage, save.ProgressionRound)
                + " · " + string.Format(GameData.L("save.progressHp"), Math.Ceiling(save.ProgressionHp), Math.Ceiling(save.ProgressionMaxHp));
        }

        /// <summary>팝업 UI를 최초 표시 때 공용 패널·버튼·글꼴 자산으로 만든다.</summary>
        private void BuildIfNeeded()
        {
            if (_isBuilt) return;
            UiFactory factory = new UiFactory(_font, _panelPrefab, _buttonPrefab);
            Image shade = factory.Panel(transform, 0, 0, UiTheme.SCREEN_WIDTH, UiTheme.SCREEN_HEIGHT, "Shade").GetComponent<Image>();
            shade.sprite = null;
            shade.color = new Color(0.025f, 0.055f, 0.09f, 0.86f);
            shade.raycastTarget = true;

            RectTransform panel = factory.Panel(transform, 230, 78, 820, 564, "SaveSlotsPanel");
            panel.GetComponent<Image>().sprite = _panelSprite;
            _titleLabel = factory.Text(panel, 32, 22, 756, 42, GameData.L("save.manage"), 28, FontStyles.Bold, "Title");
            _hintLabel = factory.Text(panel, 32, 68, 756, 28, GameData.L("save.slotHint"), 15, FontStyles.Normal, "Hint");

            for (int index = 0; index < SLOT_COUNT; index++)
            {
                int slot = index + 1;
                float rowY = 112 + index * 108;
                RectTransform row = factory.Panel(panel, 32, rowY, 756, 90, "SlotRow" + slot);
                Image rowImage = row.GetComponent<Image>();
                rowImage.sprite = _panelSprite;
                rowImage.raycastTarget = true;
                Button rowButton = row.gameObject.AddComponent<Button>();
                rowButton.targetGraphic = rowImage;
                rowButton.navigation = new Navigation { mode = Navigation.Mode.None };
                rowButton.onClick.AddListener(() => SelectOrCreateSlot(slot));
                _slotLabels[index] = factory.Text(row, 18, 13, 488, 64, string.Empty, 16, FontStyles.Normal, "SlotLabel");
                _selectButtons[index] = factory.Button(row, SLOT_BUTTON_X, 22, SLOT_BUTTON_WIDTH, 46, string.Empty,
                    () => SelectOrCreateSlot(slot), 14, "SelectSlot" + slot);
                _deleteButtons[index] = factory.Button(row, SLOT_BUTTON_X, 22, SLOT_BUTTON_WIDTH, 46, GameData.L("save.delete"),
                    () => ConfirmDeleteSlot(slot), 14, "DeleteSlot" + slot);
                UiFactory.ApplyDestructiveButtonStyle(_deleteButtons[index]);
            }

            factory.Button(panel, 650, 488, 138, 48, GameData.L("ui.close"), Close, 16, "CloseButton");
            _isBuilt = true;
        }

        /// <summary>팝업 용도에 맞게 슬롯을 검증해 새 게임·이어 하기·인게임 불러오기를 처리하고 관리 모드에서는 선택하지 않는다.</summary>
        private void SelectOrCreateSlot(int slot)
        {
            if (_mode == Mode.Manage) return;
            bool hasFile = SaveStore.HasSlot(slot);
            if (_mode == Mode.NewGame && hasFile)
            {
                _ui.ShowToast(GameData.L("save.occupiedSlot"));
                return;
            }
            if (_mode != Mode.NewGame && !hasFile) return;
            if (_mode != Mode.NewGame && !SaveStore.TryLoadSlot(slot, out _))
            {
                _ui.ShowToast(GameData.L("save.damagedSlot"));
                return;
            }
            if (_mode == Mode.SettingsLoad)
            {
                string confirmKey = slot == SaveStore.ActiveSlot ? "save.loadConfirm" : "save.switchConfirm";
                _ui.Confirm(GameData.L("save.load"), GameData.L(confirmKey), GameData.L("save.load"),
                    () => LoadSelectedSlot(slot), isDestructive: true);
                return;
            }

            bool isSuccessful = _mode == Mode.NewGame
                ? _session.CreateSaveSlot(slot)
                : _session.SelectSaveSlot(slot);
            if (!isSuccessful)
            {
                _ui.Alert(GameData.L("save.manage"), GameData.L(SaveStore.LastWarning == null ? "save.slotReadError" : "save.unavailable"));
                Refresh();
                return;
            }
            Close();
            _session.StartGame(false);
        }

        /// <summary>확인된 저장 슬롯을 활성 슬롯으로 전환하고 팝업을 닫은 뒤 설정 표시 갱신을 알린다.</summary>
        private void LoadSelectedSlot(int slot)
        {
            bool isLoaded = slot == SaveStore.ActiveSlot
                ? _session.LoadActiveSave()
                : _session.SelectSaveSlot(slot);
            if (!isLoaded)
            {
                _ui.Alert(GameData.L("save.load"), GameData.L("save.unavailable"));
                Refresh();
                return;
            }

            Close();
            _onSlotChanged?.Invoke();
        }

        /// <summary>지정 슬롯 삭제를 확인받고 삭제 후 목록을 갱신한다.</summary>
        private void ConfirmDeleteSlot(int slot)
        {
            _ui.Confirm(GameData.L("save.delete"), GameData.L("save.deleteConfirm"), GameData.L("save.delete"), () =>
            {
                if (!_session.DeleteSaveSlot(slot))
                {
                    _ui.Alert(GameData.L("save.delete"), GameData.L("save.unavailable"));
                    return;
                }
                Refresh();
            }, isDestructive: true);
        }

        /// <summary>슬롯 버튼의 TMP 레이블 문구를 갱신한다.</summary>
        private static void SetButtonLabel(Button button, string value)
        {
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = value;
        }
    }
}
