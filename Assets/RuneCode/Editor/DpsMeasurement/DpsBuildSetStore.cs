using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using UnityEditor;

namespace RuneCode.Editor
{
    /// <summary>
    /// Data/Sets 폴더의 측정 조합 셋 파일을 나열·읽기·저장·삭제한다. 셋 이름은 확장자를 뺀 파일 이름이고
    /// 영문·숫자·한글, '_', '-'만 쓸 수 있다. 경로는 프로젝트 루트 기준 상대 경로다.
    /// </summary>
    public static class DpsBuildSetStore
    {
        public const string DEFAULT_SET_NAME = "full";
        public const string SETS_FOLDER = "Assets/RuneCode/Editor/DpsMeasurement/Data/Sets";
        public const string OUTPUT_COPY_FILE_NAME = "build_set.json";

        private const string FILE_EXTENSION = ".json";
        private const string FILE_PATTERN = "*.json";

        /// <summary>셋 폴더의 셋 이름을 이름순으로 반환한다. 폴더가 없으면 빈 목록을 반환한다.</summary>
        public static List<string> ListNames()
        {
            var names = new List<string>();
            if (!Directory.Exists(SETS_FOLDER)) return names;
            foreach (string path in Directory.GetFiles(SETS_FOLDER, FILE_PATTERN))
                names.Add(Path.GetFileNameWithoutExtension(path));
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>셋 이름이 비어 있지 않고 글자·숫자·'_'·'-'로만 이루어졌는지 반환한다.</summary>
        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (char character in name)
                if (!char.IsLetterOrDigit(character) && character != '_' && character != '-') return false;
            return true;
        }

        /// <summary>셋 이름의 파일 경로(프로젝트 루트 기준)를 반환한다.</summary>
        public static string PathOf(string name)
        {
            return SETS_FOLDER + "/" + name + FILE_EXTENSION;
        }

        /// <summary>셋 이름이 있는지 반환한다.</summary>
        public static bool Exists(string name)
        {
            return File.Exists(PathOf(name));
        }

        /// <summary>셋 파일을 읽어 반환한다. 파일이 없으면 FileNotFoundException을 던진다.</summary>
        public static DpsBuildSet Load(string name)
        {
            return LoadFile(PathOf(name));
        }

        /// <summary>임의 경로의 셋 JSON 파일을 읽어 반환한다. 측정 결과 폴더의 사본을 읽을 때 쓴다.</summary>
        public static DpsBuildSet LoadFile(string path)
        {
            return JsonUtility.FromJson<DpsBuildSet>(File.ReadAllText(path));
        }

        /// <summary>셋을 pretty JSON으로 저장하고 에셋 데이터베이스에 가져온다. 이름이 규칙에 맞지 않으면 ArgumentException을 던진다.</summary>
        public static void Save(string name, DpsBuildSet set)
        {
            if (!IsValidName(name)) throw new ArgumentException("셋 이름은 글자·숫자·'_'·'-'만 쓸 수 있습니다: " + name);
            Directory.CreateDirectory(SETS_FOLDER);
            string path = PathOf(name);
            File.WriteAllText(path, JsonUtility.ToJson(set, true));
            AssetDatabase.ImportAsset(path);
        }

        /// <summary>셋 파일과 .meta를 에셋 데이터베이스로 지운다. 지웠으면 true를 반환한다.</summary>
        public static bool Delete(string name)
        {
            return AssetDatabase.DeleteAsset(PathOf(name));
        }

        /// <summary>측정 결과 폴더에 그 측정에 쓴 셋 사본(build_set.json)을 쓴다. 재개할 때 같은 셋을 다시 쓰기 위한 기록이다.</summary>
        public static void WriteOutputCopy(DpsBuildSet set, string outputFolder)
        {
            Directory.CreateDirectory(outputFolder);
            File.WriteAllText(Path.Combine(outputFolder, OUTPUT_COPY_FILE_NAME), JsonUtility.ToJson(set, true));
        }
    }
}
