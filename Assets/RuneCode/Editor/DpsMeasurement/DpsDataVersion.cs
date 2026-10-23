using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;

namespace RuneCode.Editor
{
    /// <summary>
    /// 측정에 쓴 게임 데이터 파일 내용을 이어 SHA-256으로 해시해 결과 파일의 데이터 버전을 만든다.
    /// 게임 데이터가 바뀌면 버전도 바뀌므로 서로 다른 시점의 측정 결과를 구분할 수 있다.
    /// </summary>
    public static class DpsDataVersion
    {
        private const int HASH_PREFIX_BYTES = 6;
        private const int HASH_PREFIX_LENGTH = 12;

        private static readonly string[] DATA_FILE_PATHS =
        {
            "Assets/RuneCode/Resources/RuneCode/tables/runes.json",
            "Assets/RuneCode/Resources/RuneCode/tables/elite_modifier_drops.json",
            "Assets/RuneCode/Resources/RuneCode/balance.json",
            "Assets/RuneCode/Resources/RuneCode/enemies.json",
        };

        /// <summary>
        /// DATA_FILE_PATHS 순서대로 게임 데이터 파일 내용을 이어 SHA-256으로 해시하고
        /// 앞 12자리 16진수 소문자를 반환한다. 파일이 없으면 File.ReadAllBytes가 FileNotFoundException을 던진다.
        /// </summary>
        public static string Compute()
        {
            using (SHA256 sha256 = SHA256.Create())
            using (MemoryStream content = new MemoryStream())
            {
                foreach (string relativePath in DATA_FILE_PATHS)
                {
                    byte[] fileContent = File.ReadAllBytes(GetFullPath(relativePath));
                    content.Write(fileContent, 0, fileContent.Length);
                }
                content.Position = 0;
                byte[] hash = sha256.ComputeHash(content);
                var text = new StringBuilder(HASH_PREFIX_LENGTH);
                for (int index = 0; index < HASH_PREFIX_BYTES; index++)
                    text.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        /// <summary>프로젝트 루트 기준 상대 경로를 실제 파일 경로로 바꾼다. Application.dataPath의 상위 폴더가 프로젝트 루트다.</summary>
        private static string GetFullPath(string relativePath)
        {
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), relativePath);
        }
    }
}
