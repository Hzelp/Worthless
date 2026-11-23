using UnityEngine;

namespace Worthless
{
    public sealed partial class MatchAuthority
    {
        private struct NpcWalk
        {
            public bool Walking;
            public Vector2 Destination;
            public float NextDecisionAt;
            public float CheckProgressAt;
            public Vector2 LastPosition;
        }

        private readonly NpcWalk[] npcWalks = new NpcWalk[SeatCount];
        private readonly RaycastHit2D[] npcPathHits = new RaycastHit2D[32];
        private System.Random npcMovementRandom;

        private void ResetNpcWalks()
        {
            npcMovementRandom = new System.Random(unchecked((int)gameId.Value));
            for (var i = 0; i < npcWalks.Length; i++)
                npcWalks[i] = new NpcWalk { NextDecisionAt = Time.unscaledTime + NpcDelay(0.6f, 3.8f) };
        }

        private float NpcDelay(float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, (float)npcMovementRandom.NextDouble());
        }

        private void UpdateNpcWalk(int seat, SeatActor actor, float now)
        {
            var body = actor.Body;
            if (body == null || actor.BodyCollider == null || npcMovementRandom == null) return;
            // During shared screen presentations players do not need NPCs to keep shifting around them.
            var director = RoundDirector;
            if (director == null || !Worthless.Lobby.RoundStateMachine.IsTradePhase(director.Phase))
            {
                body.velocity = Vector2.zero;
                if (npcWalks[seat].Walking) StopNpcWalk(ref npcWalks[seat], now);
                return;
            }

            ref var walk = ref npcWalks[seat];
            var position = body.position;
            if (!walk.Walking && now >= walk.NextDecisionAt)
            {
                var extents = actor.BodyCollider.bounds.extents;
                var radius = Mathf.Max(extents.x, extents.y) + 0.05f;
                for (var attempt = 0; attempt < 12; attempt++)
                {
                    var angle = NpcDelay(0f, 2f * Mathf.PI);
                    var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    var distance = NpcDelay(1.8f, 5.8f);
                    if (!NpcPathClear(actor, position, direction, distance, radius)) continue;
                    walk.Destination = position + direction * distance;
                    walk.LastPosition = position;
                    walk.CheckProgressAt = now + 0.45f;
                    walk.Walking = true;
                    break;
                }
                if (!walk.Walking) StopNpcWalk(ref walk, now);
            }

            if (!walk.Walking) { body.velocity = Vector2.zero; return; }
            var displacement = walk.Destination - position;
            if (now >= walk.CheckProgressAt)
            {
                if ((walk.LastPosition - position).sqrMagnitude < 0.08f * 0.08f)
                {
                    StopNpcWalk(ref walk, now);
                    body.velocity = Vector2.zero;
                    return;
                }
                walk.LastPosition = position;
                walk.CheckProgressAt = now + 0.45f;
            }
            if (displacement.sqrMagnitude < 0.12f * 0.12f)
            {
                StopNpcWalk(ref walk, now);
                body.velocity = Vector2.zero;
                return;
            }
            body.velocity = Vector2.ClampMagnitude(displacement / Time.fixedDeltaTime, moveSpeed * 0.46f);
        }

        private void StopNpcWalk(ref NpcWalk walk, float now)
        {
            walk.Walking = false;
            walk.NextDecisionAt = now + NpcDelay(0.8f, 3.6f);
        }

        private bool NpcPathClear(SeatActor actor, Vector2 position, Vector2 direction, float distance, float radius)
        {
            var director = RoundDirector;
            for (var room = 0; room < RoomCount; room++)
            {
                if (!director.TryGetRoomGeometry(room, out var center, out var half)) continue;
                var roomBounds = new Bounds(center, new Vector3(2f * (half.x + radius), 2f * (half.y + radius), 2f));
                var route = new Ray(position, direction);
                if (roomBounds.IntersectRay(route, out var entry) && entry <= distance) return false;
            }
            var filter = new ContactFilter2D { useTriggers = false };
            var hits = Physics2D.CircleCast(position, radius, direction, filter, npcPathHits, distance);
            if (hits == npcPathHits.Length) return false;
            for (var i = 0; i < hits; i++)
            {
                var obstacle = npcPathHits[i].collider;
                if (obstacle == actor.BodyCollider || obstacle.attachedRigidbody == actor.Body) continue;
                return false;
            }
            return true;
        }
    }
}
