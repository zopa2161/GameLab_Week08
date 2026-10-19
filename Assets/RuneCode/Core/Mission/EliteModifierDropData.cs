using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>엘리트 Modifier 등급 확률표의 한 항목이다. Percent의 합은 100이다.</summary>
    public readonly struct EliteModifierGradeChance
    {
        private readonly string _grade;
        private readonly int _percent;
        public string Grade => _grade;
        public int Percent => _percent;

        /// <summary>등급 문자와 퍼센트 확률로 항목을 만든다.</summary>
        internal EliteModifierGradeChance(string grade, int percent) { _grade = grade; _percent = percent; }
    }

    /// <summary>스테이지 클리어 Modifier 선택 보상 설정이다. 후보 등급과 제시 수, 선택 수, 슬롯별 등급 확률을 담는다.</summary>
    public sealed class EliteModifierClearChoice
    {
        private readonly string _grade;
        private readonly int _choiceCount;
        private readonly int _selectCount;
        private readonly IReadOnlyList<EliteModifierGradeChance> _slotGradeChances;
        public string Grade => _grade;
        public int ChoiceCount => _choiceCount;
        public int SelectCount => _selectCount;

        /// <summary>후보 슬롯마다 따로 굴리는 등급 확률(C→S 순서)이다. null이면 모든 슬롯이 Grade를 쓴다.</summary>
        public IReadOnlyList<EliteModifierGradeChance> SlotGradeChances => _slotGradeChances;

        /// <summary>후보 등급, 제시 수, 선택 수와 선택적인 슬롯별 등급 확률로 선택 보상 설정을 만든다.</summary>
        internal EliteModifierClearChoice(string grade, int choiceCount, int selectCount,
            IReadOnlyList<EliteModifierGradeChance> slotGradeChances = null)
        { _grade = grade; _choiceCount = choiceCount; _selectCount = selectCount; _slotGradeChances = slotGradeChances; }
    }

    /// <summary>
    /// 엘리트 Modifier 등급표 조회 창구다. 시뮬레이션은 이 창구로만 등급 확률·후보·선택 보상을 읽는다.
    /// 데이터는 tables/elite_modifier_drops.json을 기준으로 한다.
    /// </summary>
    public interface IEliteModifierDropSource
    {
        /// <summary>스테이지의 엘리트 Modifier 등급 확률(C→S 파일 순서)을 반환한다. 엘리트 Modifier 드롭이 없는 스테이지(보스)면 null을 반환하고, 표에 없는 스테이지는 마지막 일반 스테이지의 확률을 반환한다.</summary>
        IReadOnlyList<EliteModifierGradeChance> GetGradeChances(int stage);

        /// <summary>지정 등급의 값이 null이 아닌 Modifier 룬 ID 목록을 파일 순서대로 반환한다.</summary>
        IReadOnlyList<string> GetModifiersWithGrade(string grade);

        /// <summary>스테이지의 행동 드롭 등급 확률(C→S 순서)을 반환한다. 보스 스테이지면 null을 반환하고, 표에 없는 스테이지는 마지막 일반 스테이지의 확률을 반환한다.</summary>
        IReadOnlyList<EliteModifierGradeChance> GetBehaviorGradeChances(int stage);

        /// <summary>스테이지 클리어 Modifier 선택 보상 설정을 반환하고 없는 스테이지면 null을 반환한다.</summary>
        EliteModifierClearChoice GetClearChoice(int stage);
    }

    /// <summary>
    /// elite_modifier_drops.json의 단일 로더(ModifierGradeTable)를 엘리트 드롭 조회 창구로 감싼다.
    /// 21스테이지 이후는 스테이지 정의와 같이 11~20 표를 순환해 조회한다.
    /// 표에 없는 스테이지는 마지막 일반 스테이지의 확률을 쓰고(임시 규칙), 보스 스테이지는 엘리트 Modifier 드롭이 없다.
    /// </summary>
    public sealed class ModifierGradeDropSource : IEliteModifierDropSource
    {
        private readonly ModifierGradeTable _table;
        private readonly IReadOnlyList<EliteModifierGradeChance> _fallbackChances;
        private readonly IReadOnlyList<EliteModifierGradeChance> _fallbackBehaviorChances;

        /// <summary>등급표를 받아 조회 창구를 만들고 마지막 일반 스테이지의 Modifier·행동 확률을 대체값으로 잡는다.</summary>
        public ModifierGradeDropSource(ModifierGradeTable table)
        {
            _table = table;
            int fallbackStage = 0;
            foreach (ModifierStageDropTable stage in table.StageDropTables)
                if (stage.StageType == "normal" && stage.Stage > fallbackStage) fallbackStage = stage.Stage;
            ModifierStageDropTable fallback = fallbackStage > 0 ? table.GetStageDropTable(fallbackStage) : null;
            _fallbackChances = fallback != null ? ToChances(fallback.EliteModifierGradeChancesPercent, _table.GradeOrder) : null;
            _fallbackBehaviorChances = fallback != null ? ToChances(fallback.BehaviorGradeChancesPercent, GameData.Runes.BehaviorGradeOrder) : null;
        }

        /// <summary>스테이지의 등급 확률을 등급 순서대로 반환한다. 보스 스테이지는 null, 표에 없는 스테이지는 마지막 일반 스테이지 확률이다.</summary>
        public IReadOnlyList<EliteModifierGradeChance> GetGradeChances(int stage)
        {
            ModifierStageDropTable table = _table.GetStageDropTable(StageCatalog.ResolveDataStage(stage));
            return table == null ? _fallbackChances : ToChances(table.EliteModifierGradeChancesPercent, _table.GradeOrder);
        }

        /// <summary>스테이지의 행동 드롭 등급 확률을 행동 등급 순서대로 반환한다. 보스 스테이지는 null, 표에 없는 스테이지는 마지막 일반 스테이지 확률이다.</summary>
        public IReadOnlyList<EliteModifierGradeChance> GetBehaviorGradeChances(int stage)
        {
            ModifierStageDropTable table = _table.GetStageDropTable(StageCatalog.ResolveDataStage(stage));
            return table == null ? _fallbackBehaviorChances : ToChances(table.BehaviorGradeChancesPercent, GameData.Runes.BehaviorGradeOrder);
        }

        /// <summary>지정 등급 값이 null이 아닌 Modifier 룬 ID를 데이터 파일 순서대로 반환한다.</summary>
        public IReadOnlyList<string> GetModifiersWithGrade(string grade) => _table.GetModifiersWithGrade(grade);

        /// <summary>스테이지 클리어 Modifier 선택 보상 설정을 반환하고 없는 스테이지면 null을 반환한다.</summary>
        public EliteModifierClearChoice GetClearChoice(int stage)
        {
            ModifierStageDropTable table = _table.GetStageDropTable(StageCatalog.ResolveDataStage(stage));
            if (table == null || !table.HasStageClearModifierReward) return null;
            List<EliteModifierGradeChance> slotChances = null;
            if (table.RewardSlotGradeChancesPercent != null)
            {
                slotChances = new List<EliteModifierGradeChance>();
                foreach (string grade in _table.GradeOrder)
                    if (table.RewardSlotGradeChancesPercent.TryGetValue(grade, out int percent)) slotChances.Add(new EliteModifierGradeChance(grade, percent));
            }
            return new EliteModifierClearChoice(table.RewardGrade, table.ChoiceCount, table.SelectCount, slotChances);
        }

        /// <summary>등급별 확률 사전을 gradeOrder 순서의 목록으로 바꾸고, 확률이 없는(보스) 스테이지면 null을 반환한다.</summary>
        private static IReadOnlyList<EliteModifierGradeChance> ToChances(IReadOnlyDictionary<string, int> percents, IReadOnlyList<string> gradeOrder)
        {
            if (percents == null || percents.Count == 0) return null;
            var chances = new List<EliteModifierGradeChance>();
            foreach (string grade in gradeOrder)
                if (percents.TryGetValue(grade, out int percent)) chances.Add(new EliteModifierGradeChance(grade, percent));
            return chances;
        }
    }
}
