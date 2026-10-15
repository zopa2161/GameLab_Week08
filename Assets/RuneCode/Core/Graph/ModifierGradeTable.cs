using System;
using System.Collections.Generic;
using System.Linq;

namespace RuneCode
{
    /// <summary>elite_modifier_drops.json의 Modifier 등급 효과와 스테이지 보상 데이터를 보관한다.</summary>
    public sealed class ModifierGradeTable
    {
        private const string TABLE_FILE = "elite_modifier_drops.json";
        private const string LAUNCH_COUNT_KEY = "_launchGradeValues";
        private const string LAUNCH_SPREAD_KEY = "_launchSpreadAngles";
        private const string SLOT_GRADE_CHANCES_KEY = "_slotGradeChancesPercent";
        private const string ELITE_GRADE_CHANCES_KEY = "_eliteModifierGradeChancesPercent";
        private const string BEHAVIOR_GRADE_CHANCES_KEY = "_behaviorGradeChancesPercent";
        private const double MAX_LAUNCH_SPREAD_ANGLE = 90d;
        private static readonly IReadOnlyDictionary<string, int> EMPTY_CHANCES = new Dictionary<string, int>(StringComparer.Ordinal);

        private readonly IReadOnlyList<string> _gradeOrder;
        private readonly IReadOnlyList<string> _modifierOrder;
        private readonly Dictionary<string, ModifierGradeEffect> _effects;
        private readonly Dictionary<int, ModifierStageDropTable> _stageDropTables;
        private readonly IReadOnlyList<string> _dataNotes;

        public IReadOnlyList<string> GradeOrder => _gradeOrder;
        public IReadOnlyCollection<ModifierStageDropTable> StageDropTables => _stageDropTables.Values;
        public IReadOnlyList<string> DataNotes => _dataNotes;

        /// <summary>검증된 등급 효과·스테이지 표와 설명 메모를 보관한다.</summary>
        private ModifierGradeTable(IReadOnlyList<string> gradeOrder, IReadOnlyList<string> modifierOrder, Dictionary<string, ModifierGradeEffect> effects,
            Dictionary<int, ModifierStageDropTable> stageDropTables, IReadOnlyList<string> dataNotes)
        {
            _gradeOrder = gradeOrder;
            _modifierOrder = modifierOrder;
            _effects = effects;
            _stageDropTables = stageDropTables;
            _dataNotes = dataNotes;
        }

        /// <summary>JSON과 룬 카탈로그에서 데이터를 읽고 검증 오류를 모두 모아 등급 표를 반환한다.</summary>
        public static ModifierGradeTable FromJson(string json, RuneCatalog runes)
        {
            var log = new TableErrorLog(TABLE_FILE);
            object rootValue;
            try
            {
                rootValue = MinimalJsonReader.Parse(json);
            }
            catch (FormatException exception)
            {
                log.Add(null, "json", exception.Message);
                log.ThrowIfAny();
                return null;
            }
            if (!TryObject(rootValue, out Dictionary<string, object> root))
            {
                log.Add(null, "root", "최상위 값은 객체여야 합니다.");
                log.ThrowIfAny();
            }

            List<string> gradeOrder = ReadGradeOrder(root, log);
            var modifierOrder = new List<string>();
            var effects = ReadEffects(root, gradeOrder, runes, modifierOrder, log);
            ValidateModifierCoverage(runes, effects, log);
            IReadOnlyList<string> behaviorGradeOrder = runes != null ? runes.BehaviorGradeOrder : gradeOrder;
            var stages = ReadStageDropTables(root, gradeOrder, behaviorGradeOrder, log);
            List<string> notes = ReadDataNotes(root, log);
            log.ThrowIfAny();
            return new ModifierGradeTable(gradeOrder, modifierOrder, effects, stages, notes);
        }

        /// <summary>지정 Modifier에 값이 있는 등급을 파일 순서대로 반환한다.</summary>
        public IReadOnlyList<string> GetAvailableGrades(string modifierId)
        {
            var grades = new List<string>();
            if (string.IsNullOrEmpty(modifierId)) return grades;
            if (!_effects.TryGetValue(modifierId, out ModifierGradeEffect effect)) return grades;
            foreach (string grade in _gradeOrder)
                if (effect.GradeValues.TryGetValue(grade, out object value) && value != null) grades.Add(grade);
            return grades;
        }

        /// <summary>지정 Modifier에서 값이 있는 가장 낮은 등급을 반환하며 등급이 없으면 빈 문자열을 반환한다.</summary>
        public string GetLowestAvailableGrade(string modifierId)
        {
            if (string.IsNullOrEmpty(modifierId)) return string.Empty;
            if (!_effects.TryGetValue(modifierId, out ModifierGradeEffect effect)) return string.Empty;
            foreach (string grade in _gradeOrder)
                if (effect.GradeValues.TryGetValue(grade, out object value) && value != null) return grade;
            return string.Empty;
        }

        /// <summary>Modifier 등급 값이 null이 아닌지 반환한다.</summary>
        public bool IsGradeAvailable(string modifierId, string grade)
        {
            if (string.IsNullOrEmpty(modifierId) || string.IsNullOrEmpty(grade)) return false;
            return _effects.TryGetValue(modifierId, out ModifierGradeEffect effect)
                && effect.GradeValues.TryGetValue(grade, out object value) && value != null;
        }

        /// <summary>Modifier의 효과 종류를 반환하며 정의가 없으면 빈 문자열을 반환한다.</summary>
        public string GetEffectType(string modifierId)
        {
            if (string.IsNullOrEmpty(modifierId)) return string.Empty;
            return _effects.TryGetValue(modifierId, out ModifierGradeEffect effect) ? effect.EffectType : string.Empty;
        }

        /// <summary>Modifier 등급의 원본 숫자·문자열 값을 반환하며 미정의 등급이면 null을 반환한다.</summary>
        public object GetGradeValue(string modifierId, string grade)
        {
            if (string.IsNullOrEmpty(modifierId) || string.IsNullOrEmpty(grade)) return null;
            return _effects.TryGetValue(modifierId, out ModifierGradeEffect effect)
                && effect.GradeValues.TryGetValue(grade, out object value) ? value : null;
        }

        /// <summary>숫자 등급 효과를 읽으며 타입이 다르거나 값이 없으면 false를 반환한다.</summary>
        public bool TryGetNumberValue(string modifierId, string grade, out float value)
        {
            value = 0f;
            if (!(GetGradeValue(modifierId, grade) is double number)) return false;
            value = (float)number;
            return true;
        }

        /// <summary>정수 등급 효과를 읽으며 숫자가 아닌 값 또는 null이면 false를 반환한다.</summary>
        public bool TryGetIntegerValue(string modifierId, string grade, out int value)
        {
            value = 0;
            if (!(GetGradeValue(modifierId, grade) is double number)) return false;
            value = (int)number;
            return true;
        }

        /// <summary>
        /// Form에 맞는 다중 개수 등급 값을 읽는다. 행동 규칙이 발사 전용 값을 쓰고 그 값이 있으면 그 값을, 아니면 공용 등급 값을 쓴다.
        /// 숫자가 아니거나 null이면 false를 반환한다.
        /// </summary>
        public bool TryGetCountValue(string modifierId, string grade, string form, out int value)
        {
            value = 0;
            if (SpellBehaviorRuleRegistry.TryGetByKind(form, out SpellBehaviorRules rules) && rules.UsesLaunchGradeValues
                && TryGetLaunchGradeValues(modifierId, out IReadOnlyDictionary<string, object> launchValues))
            {
                if (string.IsNullOrEmpty(grade) || !launchValues.TryGetValue(grade, out object launchValue) || !(launchValue is double launchNumber)) return false;
                value = (int)launchNumber;
                return true;
            }
            return TryGetIntegerValue(modifierId, grade, out value);
        }

        /// <summary>발사 Behavior 전용 등급 값이 있으면 반환한다. 없으면 false를 반환한다.</summary>
        public bool TryGetLaunchGradeValues(string modifierId, out IReadOnlyDictionary<string, object> values)
        {
            values = null;
            if (string.IsNullOrEmpty(modifierId) || !_effects.TryGetValue(modifierId, out ModifierGradeEffect effect)) return false;
            values = effect.LaunchGradeValues;
            return values != null;
        }

        /// <summary>발사 Behavior 다중 탄의 등급별 좌우 최대 퍼짐 각도(도)를 읽는다. 발사 전용 값이 없거나 null이면 false를 반환한다.</summary>
        public bool TryGetLaunchSpreadAngle(string modifierId, string grade, out float value)
        {
            value = 0f;
            if (string.IsNullOrEmpty(modifierId) || string.IsNullOrEmpty(grade)
                || !_effects.TryGetValue(modifierId, out ModifierGradeEffect effect) || effect.LaunchSpreadAngles == null
                || !effect.LaunchSpreadAngles.TryGetValue(grade, out object angle) || !(angle is double number)) return false;
            value = (float)number;
            return true;
        }

        /// <summary>관통 등급 값을 반환하고 ALL은 명명된 무제한 상한으로 바꾼다.</summary>
        public int GetPierceCount(string modifierId, string grade, int fallback)
        {
            object value = GetGradeValue(modifierId, grade);
            if (value is string text && text == "ALL") return SpellGrammar.UNLIMITED_PIERCE_COUNT;
            return value is double number ? (int)number : fallback;
        }

        /// <summary>지정 스테이지의 엘리트 Modifier 등급 확률을 퍼센트로 반환하고 보스 스테이지면 빈 사전을 반환한다.</summary>
        public IReadOnlyDictionary<string, int> GetEliteModifierGradeChancesPercent(int stage)
        {
            return _stageDropTables.TryGetValue(stage, out ModifierStageDropTable table)
                ? table.EliteModifierGradeChancesPercent : EMPTY_CHANCES;
        }

        /// <summary>지정 스테이지의 드롭 및 선택 보상 전체 설정을 반환하고 없으면 null을 반환한다.</summary>
        public ModifierStageDropTable GetStageDropTable(int stage)
        {
            return _stageDropTables.TryGetValue(stage, out ModifierStageDropTable table) ? table : null;
        }

        /// <summary>S 등급 값이 null이 아닌 Modifier ID를 데이터 파일 순서대로 반환한다.</summary>
        public IReadOnlyList<string> GetSGradeModifierCandidates() => GetModifiersWithGrade("S");

        /// <summary>지정 등급 값이 null이 아닌 Modifier ID를 데이터 파일 순서대로 반환한다. 없는 등급이면 빈 목록이다.</summary>
        public IReadOnlyList<string> GetModifiersWithGrade(string grade)
        {
            var result = new List<string>();
            foreach (string modifierId in _modifierOrder)
                if (_effects[modifierId].GradeValues.TryGetValue(grade ?? string.Empty, out object value) && value != null) result.Add(modifierId);
            return result;
        }

        /// <summary>등급 순서 목록을 문자열 배열로 검증해 읽는다.</summary>
        private static List<string> ReadGradeOrder(Dictionary<string, object> root, TableErrorLog log)
        {
            var result = new List<string>();
            if (!TryArray(Get(root, "_gradeOrder"), out List<object> rows))
            {
                log.Add(null, "_gradeOrder", "등급 순서는 문자열 배열이어야 합니다.");
                return result;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < rows.Count; index++)
            {
                if (!(rows[index] is string grade) || string.IsNullOrEmpty(grade))
                {
                    log.Add(null, "_gradeOrder[" + index + "]", "등급은 비어 있지 않은 문자열이어야 합니다.");
                    continue;
                }
                if (!seen.Add(grade)) log.Add(null, "_gradeOrder", "등급이 중복되었습니다: " + grade);
                result.Add(grade);
            }
            if (result.Count == 0) log.Add(null, "_gradeOrder", "등급이 하나 이상 있어야 합니다.");
            return result;
        }

        /// <summary>Modifier 효과 객체를 읽고 룬 참조·키 집합·값 타입을 검증한다.</summary>
        private static Dictionary<string, ModifierGradeEffect> ReadEffects(Dictionary<string, object> root,
            IReadOnlyList<string> gradeOrder, RuneCatalog runes, List<string> modifierOrder, TableErrorLog log)
        {
            var result = new Dictionary<string, ModifierGradeEffect>(StringComparer.Ordinal);
            if (!TryArray(Get(root, "_modifierGradeEffects"), out List<object> rows))
            {
                log.Add(null, "_modifierGradeEffects", "효과 정의는 배열이어야 합니다.");
                return result;
            }
            for (int index = 0; index < rows.Count; index++)
            {
                if (!TryObject(rows[index], out Dictionary<string, object> row))
                {
                    log.Add(null, "_modifierGradeEffects[" + index + "]", "효과 정의는 객체여야 합니다.");
                    continue;
                }
                string modifierId = ReadString(row, "_modifierId", null, log, null);
                if (string.IsNullOrEmpty(modifierId))
                {
                    log.Add(null, "_modifierId", "Modifier ID가 필요합니다.");
                    continue;
                }
                if (!runes.TryGet(modifierId, out RuneDefinition rune) || rune.Category != SpellGrammar.CATEGORY_MODIFIER)
                    log.Add(modifierId, "_modifierId", "runes.json에 있는 modifier 룬 ID여야 합니다.");
                if (result.ContainsKey(modifierId))
                {
                    log.Add(modifierId, "_modifierId", "Modifier 등급 효과가 중복되었습니다.");
                    continue;
                }
                string name = ReadString(row, "_name", string.Empty, log, modifierId);
                string effectType = ReadString(row, "_effectType", string.Empty, log, modifierId);
                Dictionary<string, object> values = ReadGradeValues(row, modifierId, gradeOrder, log);
                if (!HasAvailableValue(values)) log.Add(modifierId, "_gradeValues", "값이 있는 등급이 하나 이상 있어야 합니다.");
                ValidateGradeValueTypes(modifierId, effectType, values, rune, log);
                Dictionary<string, object> launchValues = ReadLaunchGradeValues(row, modifierId, effectType, gradeOrder, values, log, LAUNCH_COUNT_KEY);
                if (launchValues != null) ValidateGradeValueTypes(modifierId, effectType, launchValues, rune, log, LAUNCH_COUNT_KEY);
                Dictionary<string, object> spreadAngles = ReadLaunchGradeValues(row, modifierId, effectType, gradeOrder, values, log, LAUNCH_SPREAD_KEY);
                if (spreadAngles != null) ValidateSpreadAngles(modifierId, spreadAngles, log);
                result.Add(modifierId, new ModifierGradeEffect(name, effectType, values, launchValues, spreadAngles));
                modifierOrder.Add(modifierId);
            }
            return result;
        }

        /// <summary>모든 modifier 룬에 하나의 등급 효과 정의가 있는지 검증한다.</summary>
        private static void ValidateModifierCoverage(RuneCatalog runes, Dictionary<string, ModifierGradeEffect> effects, TableErrorLog log)
        {
            if (runes == null) return;
            foreach (RuneDefinition rune in runes.All)
                if (rune.Category == SpellGrammar.CATEGORY_MODIFIER && !effects.ContainsKey(rune.Id))
                    log.Add(rune.Id, "_modifierGradeEffects", "Modifier 등급 효과 정의가 없습니다.");
        }

        /// <summary>등급 값 객체를 읽고 _gradeOrder와 키 집합이 같은지 검사한다. key로 읽을 필드를 지정한다.</summary>
        private static Dictionary<string, object> ReadGradeValues(Dictionary<string, object> row, string modifierId,
            IReadOnlyList<string> gradeOrder, TableErrorLog log, string key = "_gradeValues")
        {
            if (!TryObject(Get(row, key), out Dictionary<string, object> values))
            {
                log.Add(modifierId, key, "등급 값은 객체여야 합니다.");
                return new Dictionary<string, object>(StringComparer.Ordinal);
            }
            var expected = new HashSet<string>(gradeOrder, StringComparer.Ordinal);
            foreach (string grade in gradeOrder)
                if (!values.ContainsKey(grade)) log.Add(modifierId, key + "." + grade, "등급 순서에 해당하는 키가 없습니다.");
            foreach (string grade in values.Keys)
                if (!expected.Contains(grade)) log.Add(modifierId, key + "." + grade, "_gradeOrder에 없는 등급 키입니다.");
            return values;
        }

        /// <summary>
        /// 선택 필드인 발사 전용 등급 값(key)을 읽는다. multipleCount에만 허용하고,
        /// 값이 있는 등급은 _gradeValues와 같아야 한다. 필드가 없거나 null이면 null을 반환한다. 값 타입은 호출부가 검사한다.
        /// </summary>
        private static Dictionary<string, object> ReadLaunchGradeValues(Dictionary<string, object> row, string modifierId,
            string effectType, IReadOnlyList<string> gradeOrder, Dictionary<string, object> gradeValues, TableErrorLog log, string key)
        {
            if (Get(row, key) == null) return null;
            if (effectType != "multipleCount")
            {
                log.Add(modifierId, key, "multipleCount 효과에만 사용할 수 있습니다.");
                return null;
            }
            Dictionary<string, object> values = ReadGradeValues(row, modifierId, gradeOrder, log, key);
            foreach (string grade in gradeOrder)
            {
                bool hasShared = gradeValues.TryGetValue(grade, out object shared) && shared != null;
                bool hasLaunch = values.TryGetValue(grade, out object launch) && launch != null;
                if (hasShared != hasLaunch) log.Add(modifierId, key + "." + grade, "값이 있는 등급은 _gradeValues와 같아야 합니다.");
            }
            return values;
        }

        /// <summary>발사 다중 퍼짐 각도가 0 이상 MAX_LAUNCH_SPREAD_ANGLE 이하의 숫자인지 검사한다.</summary>
        private static void ValidateSpreadAngles(string modifierId, Dictionary<string, object> values, TableErrorLog log)
        {
            foreach (KeyValuePair<string, object> pair in values)
            {
                if (pair.Value == null) continue;
                if (!(pair.Value is double angle) || angle < 0d || angle > MAX_LAUNCH_SPREAD_ANGLE)
                    log.Add(modifierId, LAUNCH_SPREAD_KEY + "." + pair.Key, "0에서 " + MAX_LAUNCH_SPREAD_ANGLE + " 사이의 숫자(도)여야 합니다.");
            }
        }

        /// <summary>등급 값 객체에 null이 아닌 값이 하나라도 있는지 반환한다.</summary>
        private static bool HasAvailableValue(Dictionary<string, object> values)
        {
            foreach (object value in values.Values)
                if (value != null) return true;
            return false;
        }

        /// <summary>각 등급 값의 JSON 타입과 효과 종류 규칙을 검사한다. key는 오류에 표시할 필드 이름이다.</summary>
        private static void ValidateGradeValueTypes(string modifierId, string effectType, Dictionary<string, object> values,
            RuneDefinition rune, TableErrorLog log, string key = "_gradeValues")
        {
            bool isKnownEffect = effectType == "effectMultiplier" || effectType == "multipleCount" || effectType == "pierceCount"
                || effectType == "radiusMultiplier" || effectType == "durationMultiplier" || effectType == "speedMultiplier"
                || effectType == "homingTier";
            if (!isKnownEffect) log.Add(modifierId, "_effectType", "지원하지 않는 효과 종류입니다: " + effectType);
            foreach (KeyValuePair<string, object> pair in values)
            {
                if (pair.Value == null) continue;
                string field = key + "." + pair.Key;
                if (effectType == "effectMultiplier" || effectType == "radiusMultiplier" || effectType == "durationMultiplier"
                    || effectType == "speedMultiplier")
                {
                    if (!(pair.Value is double number) || number <= 0d || number > float.MaxValue)
                        log.Add(modifierId, field, "float 범위의 양수 숫자여야 합니다.");
                }
                else if (effectType == "multipleCount")
                {
                    if (!(pair.Value is double number) || number < 1d || number > int.MaxValue || number != Math.Truncate(number))
                        log.Add(modifierId, field, "양의 정수여야 합니다.");
                }
                else if (effectType == "pierceCount")
                {
                    if (pair.Value is string text)
                    {
                        if (text != "ALL") log.Add(modifierId, field, "비음수 정수 또는 ALL이어야 합니다.");
                    }
                    else if (!(pair.Value is double number) || number < 0d || number > int.MaxValue || number != Math.Truncate(number))
                        log.Add(modifierId, field, "비음수 정수 또는 ALL이어야 합니다.");
                }
                else if (effectType == "homingTier")
                {
                    if (!(pair.Value is string tier) || rune == null || rune.FindHomingTier(tier) == null)
                        log.Add(modifierId, field, "runes.json _homingTiers에 정의된 유도 단계 문자열이어야 합니다.");
                }
            }
        }

        /// <summary>스테이지별 Modifier·행동 드롭 등급 확률과 보스 선택 보상을 같은 파일에서 읽고 검증한다. 행동 확률 키는 behaviorGradeOrder를 따른다.</summary>
        private static Dictionary<int, ModifierStageDropTable> ReadStageDropTables(Dictionary<string, object> root,
            IReadOnlyList<string> gradeOrder, IReadOnlyList<string> behaviorGradeOrder, TableErrorLog log)
        {
            var result = new Dictionary<int, ModifierStageDropTable>();
            if (!TryArray(Get(root, "_stageDropTables"), out List<object> rows))
            {
                log.Add(null, "_stageDropTables", "스테이지 표는 배열이어야 합니다.");
                return result;
            }
            for (int index = 0; index < rows.Count; index++)
            {
                if (!TryObject(rows[index], out Dictionary<string, object> row))
                {
                    log.Add(null, "_stageDropTables[" + index + "]", "스테이지 항목은 객체여야 합니다.");
                    continue;
                }
                if (!TryInteger(Get(row, "_stage"), out int stage) || stage < 1)
                {
                    log.Add(null, "_stage", "양의 정수 스테이지 번호가 필요합니다.");
                    continue;
                }
                string stageField = "stage:" + stage;
                string stageType = ReadString(row, "_stageType", string.Empty, log, stageField);
                if (stageType != "normal" && stageType != "boss") log.Add(stageField, "_stageType", "normal 또는 boss여야 합니다.");
                if (result.ContainsKey(stage))
                {
                    log.Add(stageField, "_stage", "스테이지가 중복되었습니다.");
                    continue;
                }
                IReadOnlyDictionary<string, int> chances = ReadGradeChances(Get(row, ELITE_GRADE_CHANCES_KEY),
                    stageType, gradeOrder, stageField, log, ELITE_GRADE_CHANCES_KEY);
                IReadOnlyDictionary<string, int> behaviorChances = ReadGradeChances(Get(row, BEHAVIOR_GRADE_CHANCES_KEY),
                    stageType, behaviorGradeOrder, stageField, log, BEHAVIOR_GRADE_CHANCES_KEY);
                bool hasReward = row.ContainsKey("_stageClearModifierRewardChoice") && row["_stageClearModifierRewardChoice"] != null;
                string rewardGrade = string.Empty;
                int choiceCount = 0;
                int selectCount = 0;
                string drawMethod = string.Empty;
                string candidateRule = string.Empty;
                IReadOnlyDictionary<string, int> slotChances = null;
                if (hasReward)
                {
                    if (TryObject(row["_stageClearModifierRewardChoice"], out Dictionary<string, object> reward))
                    {
                        rewardGrade = ReadString(reward, "_grade", string.Empty, log, stageField);
                        slotChances = ReadSlotGradeChances(Get(reward, SLOT_GRADE_CHANCES_KEY), gradeOrder, stageField, log);
                        choiceCount = ReadNonNegativeInteger(reward, "_choiceCount", stageField, log);
                        selectCount = ReadNonNegativeInteger(reward, "_selectCount", stageField, log);
                        drawMethod = ReadString(reward, "_drawMethod", string.Empty, log, stageField);
                        candidateRule = ReadString(reward, "_candidateRule", string.Empty, log, stageField);
                        if (!gradeOrder.Contains(rewardGrade)) log.Add(stageField, "_grade", "_gradeOrder에 있는 등급이어야 합니다.");
                        if (choiceCount < 1 || selectCount < 1 || selectCount > choiceCount)
                            log.Add(stageField, "_stageClearModifierRewardChoice", "선택 수는 1 이상이고 후보 수 이하여야 합니다.");
                        if (drawMethod != "uniformWithoutReplacement") log.Add(stageField, "_drawMethod", "uniformWithoutReplacement여야 합니다.");
                    }
                    else log.Add(stageField, "_stageClearModifierRewardChoice", "보상 설정은 객체여야 합니다.");
                }
                if (stageType == "boss" && !hasReward) log.Add(stageField, "_stageClearModifierRewardChoice", "보스 스테이지에는 선택 보상이 필요합니다.");
                if (stageType == "normal" && hasReward) log.Add(stageField, "_stageClearModifierRewardChoice", "일반 스테이지에는 보스 선택 보상을 둘 수 없습니다.");
                result.Add(stage, new ModifierStageDropTable(stage, stageType, chances, hasReward,
                    rewardGrade, choiceCount, selectCount, drawMethod, candidateRule, slotChances, behaviorChances));
            }
            return result;
        }

        /// <summary>
        /// 선택 보상의 후보 슬롯별 등급 확률(선택 필드)을 읽는다. 키는 _gradeOrder와 같고 값은 0~100 정수이며 합계는 100이어야 한다.
        /// 필드가 없거나 null이면 null을 반환하고, 이때 모든 슬롯은 _grade 등급을 쓴다.
        /// </summary>
        private static IReadOnlyDictionary<string, int> ReadSlotGradeChances(object value, IReadOnlyList<string> gradeOrder,
            string stageField, TableErrorLog log)
        {
            if (value == null) return null;
            if (!TryObject(value, out Dictionary<string, object> rows))
            {
                log.Add(stageField, SLOT_GRADE_CHANCES_KEY, "등급별 확률 객체여야 합니다.");
                return null;
            }
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            int total = 0;
            foreach (string grade in gradeOrder)
            {
                if (!rows.TryGetValue(grade, out object chanceValue) || !TryInteger(chanceValue, out int chance) || chance < 0 || chance > 100)
                {
                    log.Add(stageField, SLOT_GRADE_CHANCES_KEY + "." + grade, "0에서 100 사이의 정수여야 합니다.");
                    continue;
                }
                result[grade] = chance;
                total += chance;
            }
            foreach (string grade in rows.Keys)
                if (!gradeOrder.Contains(grade)) log.Add(stageField, SLOT_GRADE_CHANCES_KEY + "." + grade, "_gradeOrder에 없는 등급 키입니다.");
            if (total != 100) log.Add(stageField, SLOT_GRADE_CHANCES_KEY, "확률 합계는 100이어야 합니다.");
            return result;
        }

        /// <summary>일반 스테이지 확률 객체(key 필드)를 읽고 등급 키와 정수 백분율 합계를 확인한다. 일반 스테이지는 필수이고 보스 스테이지는 null이어야 한다.</summary>
        private static IReadOnlyDictionary<string, int> ReadGradeChances(object value, string stageType,
            IReadOnlyList<string> gradeOrder, string stageField, TableErrorLog log, string key)
        {
            if (value == null)
            {
                if (stageType == "normal") log.Add(stageField, key, "일반 스테이지에는 확률 객체가 필요합니다.");
                return EMPTY_CHANCES;
            }
            if (!TryObject(value, out Dictionary<string, object> rows))
            {
                log.Add(stageField, key, "확률은 등급별 객체 또는 null이어야 합니다.");
                return EMPTY_CHANCES;
            }
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            int total = 0;
            foreach (string grade in gradeOrder)
            {
                if (!rows.TryGetValue(grade, out object chanceValue))
                {
                    log.Add(stageField, key + "." + grade, "등급 확률 키가 없습니다.");
                    continue;
                }
                if (!TryInteger(chanceValue, out int chance) || chance < 0 || chance > 100)
                {
                    log.Add(stageField, key + "." + grade, "0에서 100 사이의 정수여야 합니다.");
                    continue;
                }
                result[grade] = chance;
                total += chance;
            }
            foreach (string grade in rows.Keys)
                if (!gradeOrder.Contains(grade)) log.Add(stageField, key + "." + grade, "_gradeOrder에 없는 등급 키입니다.");
            if (stageType == "normal" && total != 100) log.Add(stageField, key, "확률 합계는 100이어야 합니다.");
            if (stageType == "boss") log.Add(stageField, key, "보스 스테이지 확률은 null이어야 합니다.");
            return result;
        }

        /// <summary>데이터 참고 메모 문자열 배열을 읽는다.</summary>
        private static List<string> ReadDataNotes(Dictionary<string, object> root, TableErrorLog log)
        {
            if (!TryArray(Get(root, "_dataNotes"), out List<object> values))
            {
                log.Add(null, "_dataNotes", "메모는 문자열 배열이어야 합니다.");
                return new List<string>();
            }
            var result = new List<string>();
            foreach (object value in values)
            {
                if (value is string text) result.Add(text);
                else log.Add(null, "_dataNotes", "모든 메모는 문자열이어야 합니다.");
            }
            return result;
        }

        /// <summary>객체에서 필드를 문자열로 읽고 값이 없거나 타입이 다르면 오류를 기록한다.</summary>
        private static string ReadString(Dictionary<string, object> row, string key, string fallback, TableErrorLog log, string id)
        {
            if (row.TryGetValue(key, out object value) && value is string text) return text;
            log.Add(id, key, "문자열 값이 필요합니다.");
            return fallback;
        }

        /// <summary>객체의 필드 값을 반환하며 키가 없으면 null을 반환한다.</summary>
        private static object Get(Dictionary<string, object> row, string key)
        {
            return row.TryGetValue(key, out object value) ? value : null;
        }

        /// <summary>JSON 값을 문자열 키 객체인지 확인하고 타입 변환 결과를 반환한다.</summary>
        private static bool TryObject(object value, out Dictionary<string, object> result)
        {
            result = value as Dictionary<string, object>;
            return result != null;
        }

        /// <summary>JSON 값을 배열인지 확인하고 타입 변환 결과를 반환한다.</summary>
        private static bool TryArray(object value, out List<object> result)
        {
            result = value as List<object>;
            return result != null;
        }

        /// <summary>JSON 숫자가 범위 내 정수인지 확인하고 변환 결과를 반환한다.</summary>
        private static bool TryInteger(object value, out int result)
        {
            result = 0;
            if (!(value is double number) || number < int.MinValue || number > int.MaxValue || number != Math.Truncate(number)) return false;
            result = (int)number;
            return true;
        }

        /// <summary>필수 정수 보상 필드를 읽고 잘못되면 오류를 추가한 뒤 0을 반환한다.</summary>
        private static int ReadNonNegativeInteger(Dictionary<string, object> row, string key, string stageField, TableErrorLog log)
        {
            if (TryInteger(Get(row, key), out int value) && value >= 0) return value;
            log.Add(stageField, key, "비음수 정수여야 합니다.");
            return 0;
        }
    }

    /// <summary>Modifier 하나의 등급 이름·효과 종류·등급값과 선택적인 발사 전용 등급값을 보관한다.</summary>
    internal sealed class ModifierGradeEffect
    {
        public string Name { get; }
        public string EffectType { get; }
        public IReadOnlyDictionary<string, object> GradeValues { get; }

        /// <summary>발사 Behavior에만 쓰는 등급값이다. 없으면 null이며 발사도 GradeValues를 쓴다.</summary>
        public IReadOnlyDictionary<string, object> LaunchGradeValues { get; }

        /// <summary>발사 Behavior 다중 탄의 등급별 좌우 최대 퍼짐 각도다. 없으면 null이며 룬 기본 퍼짐 각도를 쓴다.</summary>
        public IReadOnlyDictionary<string, object> LaunchSpreadAngles { get; }

        /// <summary>검증이 끝난 Modifier 효과 값을 보관한다.</summary>
        public ModifierGradeEffect(string name, string effectType, IReadOnlyDictionary<string, object> gradeValues,
            IReadOnlyDictionary<string, object> launchGradeValues = null, IReadOnlyDictionary<string, object> launchSpreadAngles = null)
        {
            Name = name;
            EffectType = effectType;
            GradeValues = gradeValues;
            LaunchGradeValues = launchGradeValues;
            LaunchSpreadAngles = launchSpreadAngles;
        }
    }

    /// <summary>한 스테이지의 엘리트 Modifier·행동 드롭 등급 확률과 보스 선택 보상 규칙을 보관한다.</summary>
    public sealed class ModifierStageDropTable
    {
        private static readonly IReadOnlyDictionary<string, int> EMPTY_BEHAVIOR_CHANCES = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Stage { get; }
        public string StageType { get; }
        public IReadOnlyDictionary<string, int> EliteModifierGradeChancesPercent { get; }

        /// <summary>드롭 테이블의 등급 미지정 행동 항목에 쓰는 등급 확률이다. 보스 스테이지는 빈 사전이다.</summary>
        public IReadOnlyDictionary<string, int> BehaviorGradeChancesPercent { get; }
        public bool HasStageClearModifierReward { get; }
        public string RewardGrade { get; }
        public int ChoiceCount { get; }
        public int SelectCount { get; }
        public string DrawMethod { get; }
        public string CandidateRule { get; }

        /// <summary>선택 보상 후보 슬롯마다 따로 굴리는 등급 확률이다. 없으면 null이며 모든 슬롯이 RewardGrade를 쓴다.</summary>
        public IReadOnlyDictionary<string, int> RewardSlotGradeChancesPercent { get; }

        /// <summary>검증이 끝난 스테이지 드롭 및 선택 보상 값을 보관한다.</summary>
        internal ModifierStageDropTable(int stage, string stageType, IReadOnlyDictionary<string, int> chances,
            bool hasReward, string rewardGrade, int choiceCount, int selectCount, string drawMethod, string candidateRule,
            IReadOnlyDictionary<string, int> slotGradeChances = null, IReadOnlyDictionary<string, int> behaviorGradeChances = null)
        {
            RewardSlotGradeChancesPercent = slotGradeChances;
            BehaviorGradeChancesPercent = behaviorGradeChances ?? EMPTY_BEHAVIOR_CHANCES;
            Stage = stage;
            StageType = stageType;
            EliteModifierGradeChancesPercent = chances;
            HasStageClearModifierReward = hasReward;
            RewardGrade = rewardGrade;
            ChoiceCount = choiceCount;
            SelectCount = selectCount;
            DrawMethod = drawMethod;
            CandidateRule = candidateRule;
        }
    }
}
