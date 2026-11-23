using System;

namespace Worthless.Lobby
{
    /// <summary>移动输入的服务端校验规则：非有限数、归一化、死区、限频、过期。</summary>
    public static class MoveInputRules
    {
        public const float Deadzone = 0.05f;
        public const float MinIntervalSeconds = 0.04f;
        public const float FreshSeconds = 0.25f;

        public static bool TrySanitize(float x, float y, out float normalizedX, out float normalizedY)
        {
            normalizedX = 0f;
            normalizedY = 0f;

            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y))
            {
                return false;
            }

            var magnitude = (float)Math.Sqrt(x * x + y * y);
            if (magnitude < Deadzone) return true;
            if (magnitude > 1f)
            {
                normalizedX = x / magnitude;
                normalizedY = y / magnitude;
                return true;
            }

            normalizedX = x;
            normalizedY = y;
            return true;
        }

        public static bool IsRateLimited(float nowSeconds, float lastAcceptedSeconds)
        {
            return (nowSeconds - lastAcceptedSeconds) < MinIntervalSeconds;
        }

        public static bool IsFresh(float nowSeconds, float lastInputSeconds)
        {
            return (nowSeconds - lastInputSeconds) <= FreshSeconds;
        }
    }
}