using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Worthless.Lobby;

namespace Worthless
{
    public sealed partial class MatchRoundDirector
    {
        private readonly NetworkVariable<int> configuredRounds = new NetworkVariable<int>(RoundConfigData.DefaultRounds);
        private SecretGoals secretGoals;
        private SecretSelections secretSelections;
        private uint privateGoalGame;
        private int privateGoalSeat = -1;
        private GoalGroup privateGroup;
        private int privateGoalRank;
        private uint localChoiceGame;
        private int localChoiceSeat;
        private int localChoiceRound;
        private RoundPhase localChoicePhase;
        private int localChoiceTarget;
        private int localChoiceChange;

        public int TotalRounds => configuredRounds.Value;

        public void ServerSetTotalRounds(int rounds)
        {
            if (!IsServer || initialized) return;
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (authority == null || authority.GameplayLoading || authority.Started) return;
            configuredRounds.Value = Mathf.Clamp(rounds, 1, RoundConfigData.MaxRounds);
        }

        public bool TryGetLocalGoal(out GoalGroup group, out int rank)
        {
            group = privateGroup;
            rank = privateGoalRank;
            return authority != null && rank > 0 && authority.Started && authority.GameId == privateGoalGame && authority.LocalSeat == privateGoalSeat;
        }

        private ClientRpcParams SecretRecipient(ulong client) => new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { client } }
        };

        private void SendSecretState(int seat)
        {
            if (secretGoals == null || !TryGetSeatClient(seat, out var client)) return;
            var group = secretGoals.GroupOf(seat);
            var recipient = SecretRecipient(client);
            RecipientGoalClientRpc(authority.GameId, seat, (byte)group, secretGoals.RankFor(group), recipient);
            if (RoundStateMachine.IsSecretPhase(machine.Phase) && secretSelections != null && secretSelections.TryGet(seat, out var target, out var change))
                RecipientChoiceClientRpc(authority.GameId, seat, machine.Round, (int)machine.Phase, target, change, recipient);
        }

        [ClientRpc]
        private void RecipientGoalClientRpc(uint game, int seat, byte group, int rank, ClientRpcParams recipient = default)
        {
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            privateGoalGame = game;
            privateGoalSeat = seat;
            privateGroup = (GoalGroup)group;
            privateGoalRank = rank;
        }

        public bool TryGetLocalChoice(out int target, out int change)
        {
            target = localChoiceTarget;
            change = localChoiceChange;
            return authority != null && localChoiceGame == authority.GameId && localChoiceSeat == authority.LocalSeat
                && localChoiceRound == Round && localChoicePhase == Phase && change != 0;
        }

        public void RequestSecretChoice(int target, int change)
        {
            if (authority == null || !authority.IsLocalPlayerSeated || !RoundStateMachine.IsSecretPhase(Phase)) return;
            SubmitSecretChoiceServerRpc(authority.GameId, Round, (int)Phase, target, change);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitSecretChoiceServerRpc(uint game, int round, int phaseId, int target, int change, ServerRpcParams rpc = default)
        {
            if (!initialized || authority == null || game != authority.GameId || round != machine.Round
                || phaseId != (int)machine.Phase || !RoundStateMachine.IsSecretPhase(machine.Phase) || secretSelections == null) return;
            var client = rpc.Receive.SenderClientId;
            var seat = SeatForClient(client);
            var now = ServerNow();
            if (seat < 0 || now >= machine.PhaseEndsAt) return;
            secretSelections.TrySubmit(seat, target, change, now);
            if (!secretSelections.TryGet(seat, out var acceptedTarget, out var acceptedChange)) return;
            RecipientChoiceClientRpc(game, seat, round, phaseId, acceptedTarget, acceptedChange, SecretRecipient(client));
            if (secretSelections.Complete) machine.SetPhaseEnd(now);
        }

        [ClientRpc]
        private void RecipientChoiceClientRpc(uint game, int seat, int round, int phaseId, int target, int change, ClientRpcParams recipient = default)
        {
            localChoiceGame = game;
            localChoiceSeat = seat;
            localChoiceRound = round;
            localChoicePhase = (RoundPhase)phaseId;
            localChoiceTarget = target;
            localChoiceChange = change;
        }

        private void BeginSecretChoices()
        {
            secretSelections = new SecretSelections(tier, machine.PhaseEndsAt);
            var random = new BroadcastRandom(NextRandom);
            for (var seat = 0; seat < tier; seat++) if (npc[seat]) secretSelections.FillRandom(seat, random);
            if (secretSelections.Complete) machine.SetPhaseEnd(ServerNow());
        }

        private void ApplySecretDeltas(int[] deltas)
        {
            for (var i = 0; i < tier; i++) values[i] = EconomyRules.ApplyDelta(values[i], deltas[i], economyData.MinValue, economyData.MaxValue);
            valueBatchVersion++;
            for (var i = 0; i < publicItems.Count; i++)
            {
                var item = publicItems[i];
                valueVersion[item.Seat] = valueBatchVersion;
                item.ValueVersion = valueBatchVersion;
                publicItems[i] = item;
            }
            SendPrivateToAll();
            for (var seat = 0; seat < tier; seat++)
            {
                heldHistory.RecordSettlement(seat, machine.Round, machine.TotalRounds, tieBreakInProgress,
                    itemInstance[seat], defIndex[seat], values[seat]);
                SendHistoryToSeat(seat);
            }
        }

        private void CheckFinalTie()
        {
            if (machine.Round != machine.TotalRounds) return;
            if (secretGoals.Winners(values, GoalGroup.High).Length > 1 || secretGoals.Winners(values, GoalGroup.Low).Length > 1)
                machine.RequireTieBreak();
        }

        private void PublishFinalResults()
        {
            var text = new StringBuilder();
            AppendWinners(text, GoalGroup.High, "高价组 · 从高往低第 ");
            text.AppendLine();
            AppendWinners(text, GoalGroup.Low, "低价组 · 从低往高第 ");
            resultBanner.Value = new FixedString4096Bytes(text.ToString());
        }

        private void AppendWinners(StringBuilder text, GoalGroup group, string label)
        {
            text.Append(label).Append(secretGoals.RankFor(group)).AppendLine(" 名");
            var winners = secretGoals.Winners(values, group);
            for (var i = 0; i < winners.Length; i++)
            {
                if (i > 0) text.Append("、");
                var seat = winners[i];
                text.Append(authority.PlayerNameForSeat(seat)).Append("（P").Append(seat + 1).Append("）");
            }
            if (winners.Length > 1) text.Append("  共同获胜");
        }
    }
}
