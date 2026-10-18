using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    /// <summary>외부 JSON의 배열 순서대로 표시하는 게임 도움말 목록이다.</summary>
    [Serializable]
    public sealed class EncyclopediaData
    {
        [Header("도움말 목록")]
        [SerializeField] private EncyclopediaEntry[] _entries;

        public IReadOnlyList<EncyclopediaEntry> Entries => _entries;
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

        public string Body => _body;
        public string Video => _video;
        public string Image => _image;
    }
}
