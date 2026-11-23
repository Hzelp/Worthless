using Unity.Netcode;
using UnityEngine;
using Worthless.Lobby;
namespace Worthless
{
    public sealed partial class MatchRoundDirector
    {
        private readonly System.Collections.Generic.HashSet<int> skipVotes = new System.Collections.Generic.HashSet<int>();
        private readonly NetworkVariable<int> skipVoteCount = new NetworkVariable<int>(0);
        private bool localSkipVote;
        public int SkipVoteCount { get { return skipVoteCount.Value; } }
        public bool LocalSkipVoted { get { return localSkipVote; } }

        public void ToggleSkipVote()
        {
            if (!IsSpawned || authority == null || authority.IsLocalSpectator || !authority.IsLocalPlayerSeated) return;
            localSkipVote = !localSkipVote;
            ToggleSkipVoteServerRpc(authority.GameId, Round, (int)Phase, localSkipVote);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ToggleSkipVoteServerRpc(uint gameId, int round, int phase, bool pressed, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || authority == null || !initialized || gameId != authority.GameId) return;
            if (round != Round || phase != (int)Phase || !RoundStateMachine.IsTradePhase(Phase)) return;
            CleanSkipVotes();
            var seat = SeatForClient(rpcParams.Receive.SenderClientId);
            if (seat < 0 || !alive[seat] || authority.IsSpectator(seat)) return;
            if (pressed) skipVotes.Add(seat); else skipVotes.Remove(seat);
            skipVoteCount.Value = skipVotes.Count;
            var reals = 0; var yes = 0;
            for (var i = 0; i < tier; i++)
            {
                bool npc; bool online; ulong bound;
                if (!authority.TryGetSeatInfo(i, out npc, out online, out bound) || npc || !online || !alive[i] || authority.IsSpectator(i)) continue;
                reals++;
                if (skipVotes.Contains(i)) yes++;
            }
            if (reals > 0 && yes == reals) machine.RequestEarlyEnd(ServerNow(), 0.25f);
            Debug.Log("[SkipVote] seat=" + (seat + 1) + " pressed=" + pressed + " yes=" + yes + "/" + reals);
        }

        public void ResetLocalSkipOnPhase() { localSkipVote = false; }

        private void CleanSkipVotes()
        {
            if (!IsServer || authority == null) return;
            var removed = false;
            foreach (var seat in new System.Collections.Generic.List<int>(skipVotes))
            {
                bool npc; bool online; ulong client;
                if (!authority.TryGetSeatInfo(seat, out npc, out online, out client) || npc || !online || !alive[seat] || authority.IsSpectator(seat))
                { skipVotes.Remove(seat); removed = true; }
            }
            if (removed || skipVoteCount.Value != skipVotes.Count) skipVoteCount.Value = skipVotes.Count;
        }

        private void ResetSkipVotes()
        {
            skipVotes.Clear(); skipVoteCount.Value = 0;
        }
    }
}