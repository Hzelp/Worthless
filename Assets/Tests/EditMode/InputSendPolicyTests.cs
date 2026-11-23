using NUnit.Framework;
using Worthless.Lobby;

namespace Worthless.Tests
{
    public class InputSendPolicyTests
    {
        [Test]
        public void HeldDirection_RenewsAtFixedCadence()
        {
            Assert.IsTrue(InputSendPolicy.ShouldSend(10f, 9.9f, 1f, 0f, 1f, 0f, true, false, false), "到点必须续约");
            Assert.IsFalse(InputSendPolicy.ShouldSend(10f, 9.99f, 1f, 0f, 1f, 0f, true, false, false), "间隔内不重复发送");
        }

        [Test]
        public void Idle_DoesNotSpamZero()
        {
            Assert.IsFalse(InputSendPolicy.ShouldSend(10f, 9.9f, 0f, 0f, 0f, 0f, true, false, false));
        }

        [Test]
        public void Release_SendsZeroImmediately()
        {
            Assert.IsTrue(InputSendPolicy.ShouldSend(10f, 9.99f, 0f, 0f, 1f, 0f, true, false, false));
        }

        [Test]
        public void LostFocus_SendsZeroImmediately()
        {
            Assert.IsTrue(InputSendPolicy.ShouldSend(10f, 9.99f, 1f, 0f, 1f, 0f, false, false, false));
            Assert.IsFalse(InputSendPolicy.ShouldSend(10f, 9.99f, 0f, 0f, 0f, 0f, false, false, false));
        }

        [Test]
        public void TypingInUi_SendsZeroImmediately()
        {
            Assert.IsTrue(InputSendPolicy.ShouldSend(10f, 9.99f, 0f, 1f, 0f, 1f, true, true, false));
        }

        [Test]
        public void DevOverride_UnfocusedHeldDirection_KeepsRenewing()
        {
            // 失焦 + Dev 注入：已发出的非零方向必须持续续约，而不是只发一次。
            Assert.IsTrue(InputSendPolicy.ShouldSend(10f, 9.9f, -1f, 0f, -1f, 0f, false, false, true), "失焦 Dev 续约");
            Assert.IsFalse(InputSendPolicy.ShouldSend(10.02f, 10f, -1f, 0f, -1f, 0f, false, false, true), "间隔内不重复");
            Assert.IsTrue(InputSendPolicy.ShouldSend(10.06f, 10f, -1f, 0f, -1f, 0f, false, false, true), "到点继续续约");
            Assert.IsTrue(InputSendPolicy.ShouldSend(10.26f, 10.16f, -1f, 0f, -1f, 0f, false, false, true), "持续按住一直续约");
        }

        [Test]
        public void DevOverride_UnfocusedStillSendsZeroWhenReleased()
        {
            Assert.IsTrue(InputSendPolicy.ShouldSend(10f, 9.99f, 0f, 0f, -1f, 0f, false, false, true));
            Assert.IsFalse(InputSendPolicy.ShouldSend(10f, 9.99f, 0f, 0f, 0f, 0f, false, false, true));
        }

        [Test]
        public void DevOverride_DoesNotBypassUiTyping()
        {
            Assert.IsTrue(InputSendPolicy.ShouldSend(10f, 9.99f, -1f, 0f, -1f, 0f, false, true, true));
        }
    }
}