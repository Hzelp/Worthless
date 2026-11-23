using System.Collections.Generic;

namespace Worthless.Lobby
{
    /// <summary>
    /// 房主侧的已握手客机名册。NGO 1.5.1 的断开回调先于内部名册更新，
    /// 因此计数必须基于这里“确认已握手”的集合，而不是回调里的 ConnectedClientsIds。
    /// </summary>
    public sealed class LanLobbyRoster
    {
        private readonly List<ulong> ids = new List<ulong>();

        public int Count
        {
            get { return ids.Count; }
        }

        public IReadOnlyList<ulong> Ids
        {
            get { return ids; }
        }

        /// <summary>记录一个已完成握手的客机；重复记录不会增加计数。</summary>
        public bool Add(ulong clientId)
        {
            if (ids.Contains(clientId)) return false;
            ids.Add(clientId);
            return true;
        }

        /// <summary>仅当该客机确实在名册里才移除，未完成握手的断开返回 false。</summary>
        public bool Remove(ulong clientId)
        {
            return ids.Remove(clientId);
        }

        public bool Contains(ulong clientId)
        {
            return ids.Contains(clientId);
        }

        public void Clear()
        {
            ids.Clear();
        }
    }
}