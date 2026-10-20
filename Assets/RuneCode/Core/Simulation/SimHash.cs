using System.Globalization;
using System.Text;

namespace RuneCode
{
    /// <summary>상태 해시 버퍼에 결정적 숫자·아이템 드롭 표기를 추가하는 공용 보조다.</summary>
    internal static class SimHash
    {
        /// <summary>불변 문화권 숫자 표기를 상태 해시 버퍼에 추가한다.</summary>
        internal static void AppendNumber(StringBuilder state, double value) { state.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|'); }

        /// <summary>바닥 드롭 또는 주운 Modifier 아이템의 종류, 등급, 수량, 위치를 결정성 해시 버퍼에 기록한다.</summary>
        internal static void AppendItemDropState(StringBuilder state, SimulationItemDrop drop)
        {
            state.Append('|').Append(drop.RuneId).Append('|').Append(drop.Grade).Append('|').Append(drop.Count);
            AppendNumber(state, drop.Position.X); AppendNumber(state, drop.Position.Y);
            state.Append('|').Append(drop.IsAttracted);
        }
    }
}
