namespace Worthless.Lobby
{
    /// <summary>
    /// 客机进程内的席位凭证缓存（不落盘，避免同机多实例互相读到对方凭证）。
    /// P0-02 只保证同一进程内的重连；进程重启后需要重新加入（局中会被拒绝）。
    /// </summary>
    public static class GameSessionCache
    {
        public static uint GameId { get; private set; }
        public static byte Seat { get; private set; }
        public static byte[] Payload { get; private set; }

        public static bool HasCredential
        {
            get { return Payload != null && Payload.Length > 0; }
        }

        public static void Store(uint gameId, byte seat, byte[] payload)
        {
            GameId = gameId;
            Seat = seat;
            Payload = payload;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>仅 Dev 验收桥使用：注入外部提供的凭证载荷（不提供读取接口）。</summary>
        public static void StoreRaw(byte[] payload)
        {
            Payload = payload;
        }
#endif

        public static void Clear()
        {
            GameId = 0;
            Seat = 0;
            Payload = null;
        }
    }
}