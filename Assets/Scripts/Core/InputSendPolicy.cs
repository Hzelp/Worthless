namespace Worthless.Lobby
{
    /// <summary>本地移动输入的发送调度：固定间隔续约、释放/失焦/输入占用立即清零。</summary>
    public static class InputSendPolicy
    {
        public const float SendInterval = 0.05f;

        /// <summary>
        /// devOverride 仅由 Development/Editor 的验收注入置 true：它替代窗口焦点，让已发出的非零方向持续续约；
        /// 不改变“释放立即清零”和“静止不刷零”。
        /// </summary>
        public static bool ShouldSend(float now, float lastSendTime, float currentX, float currentY, float lastX, float lastY, bool focused, bool typing, bool devOverride)
        {
            var currentZero = currentX == 0f && currentY == 0f;
            var lastZero = lastX == 0f && lastY == 0f;
            var blocked = typing || (!focused && !devOverride);

            if (blocked)
            {
                return !lastZero;
            }

            // 松开按键必须立刻清零，不受发送间隔限制。
            if (currentZero && !lastZero) return true;

            if (now - lastSendTime < SendInterval) return false;
            if (currentZero && lastZero) return false;
            return true;
        }
    }
}