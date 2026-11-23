namespace Worthless.Lobby
{
    /// <summary>权威回合阶段（纯规则，传输/界面无关）。</summary>
    public enum RoundPhase
    {
        Idle = 0,
        RoundStart = 1,
        FirstTrade = 2,
        Broadcast = 3,
        SecondTrade = 4,
        LieReveal = 5,
        ThirdTrade = 6,
        SecretChoice = 7,
        MarketUpdate = 8,
        GameOver = 9,
        TieBreak = 10,
        FinalSettlement = 11
    }
}
