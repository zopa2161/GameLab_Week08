using System.Collections.Generic;
using System.IO;

using UnityEngine;

using TMPro;
using UnityEditor;
using UnityEngine.UI;

namespace RuneCode.EditorTools
{
    /// <summary>기존 런 결과 Prefab의 빌드·전투 기록 레이아웃과 참조를 베이크한다.</summary>
    public static class RunResultPopupLayout
    {
        private const string PREFAB_PATH = "Assets/RuneCode/Resources/RuneCode/UI/RunResultPopup.prefab";
        private const float MARGIN = 40f;
        private const float CARD_TOP = 126f;
        private const float CARD_HEIGHT = 488f;
        private static readonly Color _textColor = new Color(0.88f, 0.93f, 0.96f);
        private static readonly Color _mutedColor = new Color(0.52f, 0.64f, 0.72f);
        private static readonly Color _accentColor = new Color(0.46f, 0.88f, 0.87f);

        /// <summary>기존 Prefab과 글꼴·공통 패널·버튼을 재사용해 결과 화면을 저장한다. 루트 GUID는 유지한다.</summary>
        [MenuItem("RuneCode/UI/Rebuild Run Result Popup")]
        public static void Rebuild()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            try
            {
                RunResultPopup view = root.GetComponent<RunResultPopup>();
                var serialized = new SerializedObject(view);
                TMP_FontAsset font = ((TextMeshProUGUI)serialized.FindProperty("_titleLabel").objectReferenceValue).font;
                UiFactory.ClearChildren(root.transform);
                var factory = new UiFactory(font,
                    AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RuneCode/Prefabs/UI/UiPanel.prefab"),
                    AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RuneCode/Prefabs/UI/UiButton.prefab"),
                    InstantiateElement);
                root.GetComponent<Image>().color = new Color(0.012f, 0.024f, 0.04f, 0.95f);
                var refs = new Dictionary<string, Object>();
                refs["_titleLabel"] = Label(factory, root.transform, MARGIN, 34, 700, 50, "런 종료", 38, _textColor);
                Label(factory, root.transform, MARGIN, 88, 720, 25, "마지막 빌드를 돌아보고, 다음 런을 준비하세요.", 15, _mutedColor);
                refs["_stageLabel"] = Label(factory, root.transform, 800, 42, 440, 28, "도달 스테이지  —", 20, _accentColor, true);
                refs["_playTimeLabel"] = Label(factory, root.transform, 800, 80, 440, 24, "전투 시간  —", 16, _mutedColor, true);

                RectTransform build = factory.Panel(root.transform, MARGIN, CARD_TOP, 740, CARD_HEIGHT, "BuildCard");
                Label(factory, build, 24, 18, 490, 22, "마지막으로 사용한 빌드", 14, _mutedColor);
                refs["_buildNameLabel"] = Label(factory, build, 24, 47, 490, 32, "빌드", 24, _textColor);
                Button reset = factory.Button(build, 572, 25, 144, 34, "전체 보기", size: 13, name: "ResetBuildView");
                refs["_resetViewButton"] = reset;
                RectTransform viewport = UiFactory.Rect(build, "BuildViewport", 24, 96, 692, 322);
                viewport.gameObject.AddComponent<RectMask2D>();
                RectTransform graphic = UiFactory.Rect(viewport, "BuildGraph", 0, 0, 692, 322);
                RuneGraphPreview preview = graphic.gameObject.AddComponent<RuneGraphPreview>();
                var previewProperties = new SerializedObject(preview);
                previewProperties.FindProperty("_style").objectReferenceValue = Resources.Load<GraphVisualStyle>("RuneCode/UI/Elements/GraphVisualStyle");
                previewProperties.ApplyModifiedPropertiesWithoutUndo();
                refs["_buildPreview"] = preview;
                refs["_buildMetaLabel"] = Label(factory, build, 24, 437, 400, 25, "RAM —  ·  노드 —  ·  연결 —", 15, _accentColor);
                Label(factory, build, 416, 440, 300, 22, "드래그 이동 · 휠 확대/축소", 12, _mutedColor, true);

                RectTransform stats = factory.Panel(root.transform, 800, CARD_TOP, 440, CARD_HEIGHT, "CombatStatsCard");
                Label(factory, stats, 24, 21, 392, 30, "이번 런의 전투 기록", 22, _textColor);
                Label(factory, stats, 24, 75, 392, 24, "적에게 가한 피해", 15, _mutedColor);
                refs["_damageLabel"] = Label(factory, stats, 24, 106, 392, 66, "—", 50, new Color(1f, 0.82f, 0.42f), true);
                RectTransform line = UiFactory.Rect(stats, "Divider", 24, 186, 392, 1);
                line.gameObject.AddComponent<Image>().color = new Color(0.2f, 0.33f, 0.4f);
                string[] fields = { "_killLabel", "_damageTakenLabel", "_shieldLabel", "_dpsLabel", "_energyLabel", "_dashLabel" };
                string[] labels = { "처치한 적", "받은 HP 피해", "보호막으로 막은 피해", "평균 초당 피해", "소모 마나", "대시 횟수" };
                for (int index = 0; index < fields.Length; index++)
                {
                    float y = 206 + index * 40;
                    Label(factory, stats, 24, y, 245, 30, labels[index], 16, _mutedColor);
                    refs[fields[index]] = Label(factory, stats, 266, y, 150, 30, "—", 22, _textColor, true);
                }
                Label(factory, stats, 24, 455, 392, 20, "평균 피해는 이번 런의 전투 시간을 기준으로 계산합니다.", 11, _mutedColor);
                Button title = factory.Button(root.transform, 1050, 646, 190, 40, "타이틀로 돌아가기", size: 14, name: "ReturnToTitle");
                title.navigation = new Navigation { mode = Navigation.Mode.None };
                refs["_titleButton"] = title;
                serialized.Update();
                foreach (KeyValuePair<string, Object> reference in refs)
                    serialized.FindProperty(reference.Key).objectReferenceValue = reference.Value;
                serialized.FindProperty("_isEscapeClosable").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log("Run result layout saved: " + PREFAB_PATH);
        }

        /// <summary>공통 UI 요소를 Prefab 연결을 유지한 채 지정한 부모 아래 생성한다.</summary>
        private static GameObject InstantiateElement(GameObject asset, Transform parent)
            => (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);

        /// <summary>결과 Prefab을 베이크하고 기본 마법을 표시한 편집용 화면을 Temp에 PNG로 저장한다. 세이브·로그는 생성하지 않는다.</summary>
        [MenuItem("RuneCode/UI/Preview Run Result Popup")]
        public static void RebuildAndPreview()
        {
            Rebuild();
            GameData.Load();
            RectTransform screen = UiFactory.CreateCanvas(null, "RunResultPreviewCanvas");
            GameObject cameraObject = new GameObject("RunResultPreviewCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            var target = new RenderTexture(1920, 1080, 24);
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.02f, 0.035f, 0.055f);
                camera.orthographic = true;
                camera.orthographicSize = UiTheme.SCREEN_HEIGHT / 2;
                camera.transform.position = new Vector3(0, 0, -100);
                camera.targetTexture = target;
                Canvas canvas = screen.GetComponentInParent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10;
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH), screen);
                var view = instance.GetComponent<RunResultPopup>();
                SpellGraph graph = GameData.Spells[0].Clone();
                var unlocked = new List<string>();
                foreach (RuneDefinition rune in GameData.Runes.All) unlocked.Add(rune.Id);
                CompileResult compiled = GraphCompiler.Compile(graph, GameData.Runes, GameData.Balance.Grammar, unlocked,
                    GameData.Balance.Economy.BaseCapacity, GameData.Balance.Player.MaxEnergy, GameData.Spells);
                if (!compiled.Ok) throw new System.InvalidOperationException("기본 빌드를 컴파일하지 못했습니다.");
                var simulation = new RuneSimulation(SimulationSetup.FromGameData(1, true, GameData.Balance.Player.MaxHp,
                    GameData.Balance.Player.MaxEnergy, 1, GameData.Balance.Player.EnergyRegen, isProgressionMission: true));
                simulation.SetLoadout(new[] { compiled.Spell });
                var input = new SimulationInput(SimVector.Zero, new SimVector(1, 0), true);
                for (int tick = 0; tick < 60 * 180 && !simulation.Completed && !simulation.IsFailed && !simulation.IsWaitingForNextRound; tick++)
                    simulation.Step(input);
                Canvas.ForceUpdateCanvases();
                view.Configure(graph, GameData.Spells, compiled.Spell, simulation);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                image.Apply();
                File.WriteAllBytes("Temp/run-result-preview.png", image.EncodeToPNG());
                int missingScripts = 0;
                foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                    missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
                if (missingScripts != 0) throw new System.InvalidOperationException("Missing scripts: " + missingScripts);
                Debug.Log("Run result preview saved: Temp/run-result-preview.png; missing scripts: 0");
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(screen.GetComponentInParent<Canvas>().gameObject);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(image);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>위치·문구·크기·색과 선택적 우측 정렬을 적용한 레이블을 반환한다.</summary>
        private static TextMeshProUGUI Label(UiFactory factory, Transform parent, float x, float y, float width, float height,
            string value, float size, Color color, bool right = false)
        {
            TextMeshProUGUI text = factory.Text(parent, x, y, width, height, value, size);
            text.color = color;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            return text;
        }
    }
}
