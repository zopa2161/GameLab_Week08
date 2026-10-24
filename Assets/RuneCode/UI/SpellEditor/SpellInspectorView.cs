using System.Text;

namespace RuneCode
{
    /// <summary>
    /// 호버 설명 텍스트를 쌓는 도우미 View다. AddTitle·AddNote로 문자열만 만들고 UI는 생성하지 않는다.
    /// 무엇을 표시할지는 SpellInspectorPresenter가 정한다.
    /// </summary>
    public sealed class SpellInspectorView
    {
        private readonly StringBuilder _description = new StringBuilder();

        public string Description => _description.ToString();

        /// <summary>쌓인 설명 텍스트를 모두 비운다.</summary>
        public void Begin()
        {
            _description.Clear();
        }

        /// <summary>제목 줄을 설명에 추가한다.</summary>
        public void AddTitle(string text)
        {
            _description.Append("<b>").Append(text).AppendLine("</b>");
        }

        /// <summary>안내·정보 문구를 설명에 추가한다.</summary>
        public void AddNote(string text, float height, float spacing)
        {
            _description.AppendLine(text);
        }
    }
}
