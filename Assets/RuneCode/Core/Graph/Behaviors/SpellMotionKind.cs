namespace RuneCode
{
    /// <summary>
    /// 행동 개체의 이동 성격이다. 이동 모드(유도·공전)가 이 값으로 적용 방식을 고른다.
    /// 이동형은 시간에 따라 진행하고(발사), 정지형은 제자리에 머물며(폭발·잔류), 경로형은 매 틱 공간에 경로를 그린다(빔).
    /// </summary>
    public enum SpellMotionKind
    {
        Moving,
        Stationary,
        Path
    }
}
