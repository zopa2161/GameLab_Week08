using System;
using System.Collections.Generic;
using System.Globalization;

namespace RuneCode
{
    /// <summary>
    /// 룬 설명·등급 수치·장착 가능 행동 문구를 strings 테이블 키로 만든다.
    /// 팔레트 호버, 노드 인스펙터, 보상 카드가 같은 문구를 쓰며 효과 타입별 분기는 이 클래스에만 둔다.
    /// </summary>
    public static class RuneDescriptionBuilder
    {
        private const string DESCRIPTION_KEY_PREFIX = "rune.";
        private const string DESCRIPTION_KEY_SUFFIX = ".desc";
        private const string EFFECT_KEY_PREFIX = "rune.effect.";
        private const string DETAIL_KEY_PREFIX = "rune.detail.";
        private const string MODIFIER_TARGETS_KEY = "rune.modifierTargets";
        private const string CLEAR_MODIFIER_TARGETS_KEY = "result.clearModifierTargets";
        private const string HOMING_NONE_KEY = "rune.effect.homingNone";
        private const string LAUNCH_COUNT_KEY = "rune.effect.launchCount";
        private const string WAVE_COUNT_KEY = "rune.effect.waveCount";
        private const string NAME_SEPARATOR = " · ";
        private const string NUMBER_FORMAT = "0.##";
        private const string ANGLE_FORMAT = "0.#";
        private const string GRADE_VALUE_START = "\uE000";
        private const string GRADE_VALUE_END = "\uE001";

        // 장착 가능 행동은 행동 규칙 등록 순서와 무관하게 팔레트 순서로 표시한다.
        private static readonly string[] BEHAVIOR_DISPLAY_ORDER =
        {
            "behavior.launch", "behavior.burst", "behavior.persist", "behavior.beam", "behavior.slash"
        };
        private static readonly string[] DAMAGE_TRADE_OFF_RUNE_IDS = { "mod.multi", "mod.expand" };

        /// <summary>룬의 짧은 설명 한 문장을 rune.{id}.desc 키에서 반환한다. 화염은 전투 밸런스의 화상 수치를 넣는다.</summary>
        public static string GetDescription(RuneDefinition rune)
        {
            string key = DESCRIPTION_KEY_PREFIX + rune.Id + DESCRIPTION_KEY_SUFFIX;
            if (rune.Id != "element.fire") return GameData.L(key);
            CombatBalance combat = GameData.Balance.Combat;
            return GameData.Format(key,
                ("burnSeconds", Number(combat.BurnSeconds, ANGLE_FORMAT)),
                ("burnInterval", Number(combat.BurnInterval, ANGLE_FORMAT)),
                ("burnPercent", Number(combat.BurnDamageRatio * 100f, ANGLE_FORMAT)));
        }

        /// <summary>
        /// Modifier 등급의 효과 문구를 줄 단위로 반환한다. isDetailed면 비용·보조 수치를 더하고 colorValues면 등급 수치와 같은 표시 구간의 단위까지 등급 색으로 표시한다.
        /// 다중은 발사 전용 개수와 폭발 회차 수를 함께 표시하며, 등급 값이 없는 Modifier나 등급이 없는 룬은 빈 목록이고
        /// 유도 단계가 없는 등급은 유도 없음 한 줄이다.
        /// </summary>
        public static List<string> GetGradeLines(RuneDefinition rune, string grade, bool isDetailed, bool colorValues = false)
        {
            var lines = new List<string>();
            if (rune.Category != SpellGrammar.CATEGORY_MODIFIER || string.IsNullOrEmpty(grade)) return lines;
            ModifierGradeTable grades = GameData.ModifierGrades;
            string effectType = grades.GetEffectType(rune.Id);
            object gradeValue = grades.GetGradeValue(rune.Id, grade);
            AddEffectLines(lines, rune, grade, effectType, gradeValue, colorValues);
            if (rune.Id == "mod.multi" && grades.TryGetLaunchGradeValues(rune.Id, out _)
                && grades.TryGetCountValue(rune.Id, grade, true, out int launchCount))
                AddGradeLines(lines, GameData.Format(LAUNCH_COUNT_KEY, ("value", GradeNumber(launchCount, NUMBER_FORMAT, colorValues))), grade, colorValues);
            if (rune.Id == "mod.multi" && grades.TryGetWaveGradeCount(rune.Id, grade, out int waveCount))
                AddGradeLines(lines, GameData.Format(WAVE_COUNT_KEY, ("value", GradeNumber(waveCount, NUMBER_FORMAT, colorValues))), grade, colorValues);
            if (isDetailed) AddDetailLines(lines, rune, grade, effectType);
            return lines;
        }

        /// <summary>Modifier가 실제로 효과를 내는 행동 이름을 팔레트 순서로 모아 반환하고 Modifier가 아니면 빈 목록을 반환한다.</summary>
        public static List<string> GetModifierTargetNames(RuneDefinition rune)
        {
            var names = new List<string>();
            if (rune.Category != SpellGrammar.CATEGORY_MODIFIER) return names;
            foreach (string behaviorId in BEHAVIOR_DISPLAY_ORDER)
            {
                if (GameData.Runes.IsModifierCompatible(rune.Id, behaviorId) && GameData.Runes.TryGet(behaviorId, out RuneDefinition behavior))
                    names.Add(behavior.Name);
            }
            return names;
        }

        /// <summary>허용 행동 이름을 가운뎃점으로 묶은 장착 가능 행동 문구를 반환하고 대상이 없으면 null을 반환한다.</summary>
        public static string GetModifierTargets(RuneDefinition rune)
        {
            List<string> names = GetModifierTargetNames(rune);
            return names.Count == 0 ? null : GameData.Format(MODIFIER_TARGETS_KEY, ("targets", string.Join(NAME_SEPARATOR, names)));
        }

        /// <summary>보상 선택 카드용으로 장착 가능 행동 이름을 가운뎃점으로 묶은 두 줄 문구를 반환하고 대상이 없으면 빈 문자열을 반환한다.</summary>
        public static string GetClearChoiceModifierTargets(RuneDefinition rune)
        {
            List<string> names = GetModifierTargetNames(rune);
            return names.Count == 0 ? string.Empty
                : GameData.Format(CLEAR_MODIFIER_TARGETS_KEY, ("targets", string.Join(NAME_SEPARATOR, names)));
        }

        /// <summary>제목과 중복되는 이름·등급을 제외하고 룬의 짧은 설명, 등급별 효과 수치, 장착 가능 행동을 호버 본문으로 반환한다.</summary>
        public static string GetHoverText(RuneDefinition rune, string grade)
        {
            string text = GetDescription(rune);
            List<string> lines = GetGradeLines(rune, grade, false, true);
            if (lines.Count > 0) text += "\n" + string.Join(NAME_SEPARATOR, lines);
            string targets = GetModifierTargets(rune);
            if (targets != null) text += "\n\n" + targets;
            return text;
        }

        /// <summary>효과 문구에 등급 값과 보조 수치를 넣어 lines에 더하고 colorValues면 등급으로 정해지는 수치와 단위에 색칠한다.</summary>
        private static void AddEffectLines(List<string> lines, RuneDefinition rune, string grade, string effectType, object gradeValue, bool colorValues)
        {
            ModifierGradeTable grades = GameData.ModifierGrades;
            string key = EFFECT_KEY_PREFIX + effectType;
            switch (effectType)
            {
                case ModifierGradeTable.HOMING_TIER_EFFECT:
                    HomingTierDefinition homing = gradeValue is string tier ? rune.FindHomingTier(tier) : null;
                    if (homing == null)
                    {
                        AddLines(lines, GameData.L(HOMING_NONE_KEY));
                        return;
                    }
                    AddGradeLines(lines, GameData.Format(key,
                        ("tier", gradeValue),
                        ("turn", GradeNumber(homing.HomingTurn, ANGLE_FORMAT, colorValues)),
                        ("range", GradeNumber(homing.HomingRange, ANGLE_FORMAT, colorValues)),
                        ("targets", GradeNumber(homing.OrbitTargetCount, NUMBER_FORMAT, colorValues))), grade, colorValues);
                    return;
                case ModifierGradeTable.KNOCKBACK_DISTANCE_EFFECT:
                    if (gradeValue == null) return;
                    AddGradeLines(lines, GameData.Format(key,
                        ("damage", Number(rune.Stats.KnockbackReferenceDamage, NUMBER_FORMAT)),
                        ("value", GradeNumber(gradeValue, NUMBER_FORMAT, colorValues))), grade, colorValues);
                    return;
                case ModifierGradeTable.BEAM_EMPOWER_MULTIPLIER_EFFECT:
                    if (gradeValue == null) return;
                    AddGradeLines(lines, GameData.Format(key,
                        ("start", Number(BeamEmpowerModifierRules.CHANNEL_SECONDS, ANGLE_FORMAT)),
                        ("max", Number(BeamEmpowerModifierRules.MAX_CHANNEL_SECONDS, ANGLE_FORMAT)),
                        ("value", GradeNumber(gradeValue, NUMBER_FORMAT, colorValues)),
                        ("mana", GradeNumber(grades.GetBeamEmpowerManaMultiplier(rune.Id, grade), NUMBER_FORMAT, colorValues)),
                        ("width", GradeNumber(grades.GetBeamEmpowerSizeMultiplier(rune.Id, grade), NUMBER_FORMAT, colorValues))), grade, colorValues);
                    return;
                case ModifierGradeTable.EFFECT_MULTIPLIER_EFFECT:
                    if (gradeValue == null) return;
                    AddGradeLines(lines, GameData.Format(key, ("value", GradeNumber(gradeValue, NUMBER_FORMAT, colorValues))), grade, colorValues);
                    AddGradeLines(lines, GameData.Format(EFFECT_KEY_PREFIX + ModifierGradeTable.SPEED_MULTIPLIER_EFFECT,
                        ("value", GradeNumber(grades.GetAmplifySpeedMultiplier(rune.Id, grade), NUMBER_FORMAT, colorValues))), grade, colorValues);
                    return;
                case ModifierGradeTable.ENERGY_COST_MULTIPLIER_EFFECT:
                case ModifierGradeTable.MULTIPLE_COUNT_EFFECT:
                case ModifierGradeTable.PIERCE_COUNT_EFFECT:
                case ModifierGradeTable.RADIUS_MULTIPLIER_EFFECT:
                case ModifierGradeTable.DURATION_MULTIPLIER_EFFECT:
                case ModifierGradeTable.SPEED_MULTIPLIER_EFFECT:
                case ModifierGradeTable.ORBIT_PATH_EFFECT:
                    if (gradeValue != null) AddGradeLines(lines, GameData.Format(key, ("value", GradeNumber(gradeValue, NUMBER_FORMAT, colorValues))), grade, colorValues);
                    return;
            }
        }

        /// <summary>인스펙터용 비용 배율과 다중(폭발 회차 포함)·확장·관통·밀어내기·공전의 보조 수치를 lines에 더한다.</summary>
        private static void AddDetailLines(List<string> lines, RuneDefinition rune, string grade, string effectType)
        {
            ModifierGradeTable grades = GameData.ModifierGrades;
            RuneStats stats = rune.Stats;
            switch (effectType)
            {
                case ModifierGradeTable.KNOCKBACK_DISTANCE_EFFECT:
                    AddDetail(lines, "knockbackSeconds", Number(stats.KnockbackSeconds, NUMBER_FORMAT));
                    break;
                case ModifierGradeTable.ORBIT_PATH_EFFECT:
                    AddDetail(lines, "angularSpeed", Number(stats.AngularSpeed, ANGLE_FORMAT));
                    AddDetail(lines, "entrySpeed", Number(stats.EntrySpeed, ANGLE_FORMAT));
                    break;
            }
            if (effectType != ModifierGradeTable.ENERGY_COST_MULTIPLIER_EFFECT)
                AddDetail(lines, "costMultiplier", Number(grades.GetEnergyMultiplier(rune.Id, grade), NUMBER_FORMAT));
            // 개수·회차·면적을 늘리는 대신 피해를 줄이는 룬만 고정 피해 배율을 표시한다.
            if (Array.IndexOf(DAMAGE_TRADE_OFF_RUNE_IDS, rune.Id) >= 0 && stats.DamageMultiplier != 1f)
                AddDetail(lines, "damage", Number(stats.DamageMultiplier, NUMBER_FORMAT));
            if (rune.Id == "mod.multi")
            {
                if (stats.SpreadAngle > 0f) AddDetail(lines, "spread", Number(stats.SpreadAngle, ANGLE_FORMAT));
                if (grades.TryGetLaunchSpreadAngle(rune.Id, grade, out float launchSpread))
                    AddDetail(lines, "launchSpread", Number(launchSpread, ANGLE_FORMAT));
            }
            if (stats.WaveInterval > 0f) AddDetail(lines, "waveInterval", Number(stats.WaveInterval, NUMBER_FORMAT));
            if (grades.TryGetPierceDamageMultiplier(rune.Id, grade, out float pierceMultiplier))
                AddDetail(lines, "pierceDamage", Number(pierceMultiplier, NUMBER_FORMAT));
        }

        /// <summary>rune.detail.{name} 문구에 value를 넣어 lines에 더한다.</summary>
        private static void AddDetail(List<string> lines, string name, string value)
        {
            AddLines(lines, GameData.Format(DETAIL_KEY_PREFIX + name, ("value", value)));
        }

        /// <summary>여러 줄 문구를 줄바꿈 기준으로 나눠 lines에 더한다.</summary>
        private static void AddLines(List<string> lines, string text)
        {
            lines.AddRange(text.Split('\n'));
        }

        /// <summary>효과 문구를 줄 단위로 나눠 lines에 더한다. colorValues면 등급 수치 표식을 같은 굵은 표시 구간의 단위까지 확장해 색칠한다.</summary>
        private static void AddGradeLines(List<string> lines, string text, string grade, bool colorValues)
        {
            AddLines(lines, colorValues ? ColorizeGradeSpans(text, grade) : text);
        }

        /// <summary>문구 안의 등급 수치 표식을 같은 굵은 표시 구간 전체로 넓혀 등급 색을 입히고 표식을 지운다.</summary>
        private static string ColorizeGradeSpans(string text, string grade)
        {
            int markerStart = text.IndexOf(GRADE_VALUE_START, StringComparison.Ordinal);
            while (markerStart >= 0)
            {
                int markerEnd = text.IndexOf(GRADE_VALUE_END, markerStart, StringComparison.Ordinal);
                if (markerEnd < 0) break;
                int boldOpen = text.LastIndexOf("<b>", markerStart, StringComparison.Ordinal);
                int boldCloseBefore = boldOpen >= 0 ? text.LastIndexOf("</b>", markerStart, StringComparison.Ordinal) : -1;
                int boldClose = text.IndexOf("</b>", markerEnd, StringComparison.Ordinal);
                bool hasBoldSpan = boldOpen >= 0 && boldOpen > boldCloseBefore && boldClose >= 0;
                int start = hasBoldSpan ? boldOpen + 3 : markerStart;
                int end = hasBoldSpan ? boldClose : markerEnd + GRADE_VALUE_END.Length;
                string content = text.Substring(start, end - start)
                    .Replace(GRADE_VALUE_START, string.Empty).Replace(GRADE_VALUE_END, string.Empty);
                string colored = ModifierGradeText.Colorize(content, grade);
                text = text.Substring(0, start) + colored + text.Substring(end);
                markerStart = text.IndexOf(GRADE_VALUE_START, start + colored.Length, StringComparison.Ordinal);
            }
            return text.Replace(GRADE_VALUE_START, string.Empty).Replace(GRADE_VALUE_END, string.Empty);
        }

        /// <summary>등급 값을 지정 숫자 형식으로 반환하고 colorValues면 이후 단위까지 색칠할 수 있도록 표식으로 감싼다. ALL 같은 문자열은 그대로 반환한다.</summary>
        private static string GradeNumber(object value, string format, bool colorValues)
        {
            if (value is string text) return text;
            string number = Number(value, format);
            return colorValues ? GRADE_VALUE_START + number + GRADE_VALUE_END : number;
        }

        /// <summary>숫자 값을 지정 형식의 고정 문화권 문자열로 반환한다.</summary>
        private static string Number(object value, string format)
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
