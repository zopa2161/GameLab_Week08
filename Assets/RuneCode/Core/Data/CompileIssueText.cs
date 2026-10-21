namespace RuneCode
{
    /// <summary>문법 엔진의 컴파일 진단을 현지화된 사용자 문구로 만든다.</summary>
    public static class CompileIssueText
    {
        /// <summary>
        /// 진단 코드의 현지화 문구에 상세값과 하위 원인 문구를 이어 붙여 반환한다.
        /// 예: "다른 마법 오류 화염구: 시작 조건이 없습니다".
        /// </summary>
        public static string Format(CompileIssue issue)
        {
            string text = GameData.L("issue." + issue.Code);
            if (issue.Code == "W7" && issue.Detail != null)
            {
                int separator = issue.Detail.IndexOf('=');
                int divider = issue.Detail.IndexOf('/', separator + 1);
                if (separator > 0 && divider > separator)
                {
                    string resource = issue.Detail.Substring(0, separator);
                    text += " " + ResourceName(resource) + " " + issue.Detail.Substring(separator + 1, divider - separator - 1)
                        + "/" + issue.Detail.Substring(divider + 1);
                }
                else text += " " + issue.Detail;
            }
            else if (issue.Code == "W8" && issue.Detail != null)
            {
                text += " " + ResourceName(issue.Detail);
            }
            else if (issue.Detail != null)
            {
                text += " " + issue.Detail;
            }
            if (issue.Cause != null)
            {
                text += ": " + Format(issue.Cause);
            }
            return text;
        }

        /// <summary>자원 ID를 현지화 이름으로 바꾸고 번역이 없으면 ID를 반환한다.</summary>
        private static string ResourceName(string resource)
        {
            return GameData.TryL("resource." + resource, out string localized) ? localized : resource;
        }
    }
}
