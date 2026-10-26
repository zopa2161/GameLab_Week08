using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    /// <summary>외부 JSON의 배열 순서대로 표시하는 게임 도움말 목록이다.</summary>
    [Serializable]
    public sealed class EncyclopediaData
    {
        private const string RESOURCE_PATH = "RuneCode/encyclopedia";

        [Header("도움말 목록")]
        [SerializeField] private EncyclopediaEntry[] _entries;

        [Header("이벤트 안내")]
        [SerializeField] private EncyclopediaEvent[] _events;

        public IReadOnlyList<EncyclopediaEntry> Entries => _entries;
        public IReadOnlyList<EncyclopediaEvent> Events => _events;

        /// <summary>id가 같은 이벤트 안내를 반환하고 없으면 null을 반환한다.</summary>
        public EncyclopediaEvent FindEvent(string id)
        {
            if (_events == null) return null;
            foreach (EncyclopediaEvent item in _events)
                if (item != null && item.Id == id) return item;
            return null;
        }

        /// <summary>1부터 세는 chapter 번호의 도움말 항목을 반환하고 범위를 벗어나면 null을 반환한다.</summary>
        public EncyclopediaEntry FindChapter(int chapter)
        {
            if (_entries == null || chapter < 1 || chapter > _entries.Length) return null;
            return _entries[chapter - 1];
        }

        /// <summary>Resources의 도움말 JSON을 읽어 반환한다. 파일이 없거나 읽지 못하면 경고를 남기고 null을 반환한다.</summary>
        public static EncyclopediaData LoadResource()
        {
            TextAsset asset = Resources.Load<TextAsset>(RESOURCE_PATH);
            if (asset == null) return null;
            try
            {
                return JsonUtility.FromJson<EncyclopediaData>(asset.text);
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning("게임 도움말 JSON을 읽을 수 없습니다: " + exception.Message);
                return null;
            }
        }
    }

    /// <summary>챕터 목차에 포함되지 않고 특정 상황(예: 첫 패배)에서 한 번만 띄우는 안내다. 코드의 트리거 ID와 _id로 연결한다.</summary>
    [Serializable]
    public sealed class EncyclopediaEvent
    {
        [Header("이벤트 안내 내용")]
        [SerializeField] private string _id;
        [SerializeField] private string _title;
        [SerializeField] private EncyclopediaPage[] _pages;

        public string Id => _id;
        public string Title => _title;
        public IReadOnlyList<EncyclopediaPage> Pages => _pages;
    }

    /// <summary>목차 제목과 좌우로 넘기는 본문 페이지 목록이다.</summary>
    [Serializable]
    public sealed class EncyclopediaEntry
    {
        [Header("도움말 내용")]
        [SerializeField] private string _title;
        [SerializeField] private EncyclopediaPage[] _pages;

        public string Title => _title;
        public IReadOnlyList<EncyclopediaPage> Pages => _pages;
    }

    /// <summary>도움말 한 페이지의 설명과 Resources 기준 영상 또는 이미지 경로다. 한 페이지에는 둘 중 하나만 쓰며 둘 다 비어 있으면 설명만 표시한다.</summary>
    [Serializable]
    public sealed class EncyclopediaPage
    {
        [Header("페이지 내용")]
        [SerializeField] private string _body;
        [SerializeField] private string _video;
        [SerializeField] private string _image;
        [SerializeField] private string _secondaryImage;

        public string Body => _body;
        public string Video => _video;
        public string Image => _image;
        public string SecondaryImage => _secondaryImage;
    }
}
