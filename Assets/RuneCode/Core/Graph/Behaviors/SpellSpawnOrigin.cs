namespace RuneCode
{
    /// <summary>
    /// 행동 개체가 생성되는 위치의 유형이다. 배치 모드(다중)가 여러 개체를 어떻게 나눌지 정할 때 쓴다.
    /// 지팡이 앞은 진행 방향을 퍼짐 각도로 나누고, 지면은 생성 지점 주위에 원형으로 흩어 놓는다.
    /// 궤도는 경로 모드(공전)가 생성 위치를 정하는 경우이며, 궤도 자리를 등각으로 나누는 일은 그 모드가 한다.
    /// </summary>
    public enum SpellSpawnOrigin
    {
        Wand,
        Ground,
        Orbit
    }
}
