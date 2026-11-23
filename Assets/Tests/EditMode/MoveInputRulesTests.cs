using NUnit.Framework;
using Worthless.Lobby;

namespace Worthless.Tests
{
    public class MoveInputRulesTests
    {
        [Test]
        public void TrySanitize_RejectsNonFinite()
        {
            float x, y;
            Assert.IsFalse(MoveInputRules.TrySanitize(float.NaN, 0f, out x, out y));
            Assert.IsFalse(MoveInputRules.TrySanitize(0f, float.PositiveInfinity, out x, out y));
            Assert.AreEqual(0f, x);
            Assert.AreEqual(0f, y);
        }

        [Test]
        public void TrySanitize_NormalizesDiagonal()
        {
            float x, y;
            Assert.IsTrue(MoveInputRules.TrySanitize(1f, 1f, out x, out y));
            Assert.AreEqual(0.7071f, x, 0.0005f);
            Assert.AreEqual(0.7071f, y, 0.0005f);
        }

        [Test]
        public void TrySanitize_AppliesDeadzone()
        {
            float x, y;
            Assert.IsTrue(MoveInputRules.TrySanitize(0.01f, 0.01f, out x, out y));
            Assert.AreEqual(0f, x);
            Assert.AreEqual(0f, y);
        }

        [TestCase(0.01f)]
        [TestCase(0.03f)]
        public void IsRateLimited_RejectsTooFast(float delta)
        {
            Assert.IsTrue(MoveInputRules.IsRateLimited(100f, 100f - delta));
        }

        [TestCase(0.04f)]
        [TestCase(0.5f)]
        [TestCase(10f)]
        public void IsRateLimited_AllowsNormalCadence(float delta)
        {
            Assert.IsFalse(MoveInputRules.IsRateLimited(100f, 100f - delta));
        }

        [Test]
        public void IsRateLimited_AcceptsNormalCadence()
        {
            Assert.IsFalse(MoveInputRules.IsRateLimited(100f, 99.9f));
        }

        [Test]
        public void IsFresh_ExpiresOldInput()
        {
            Assert.IsTrue(MoveInputRules.IsFresh(10f, 9.9f));
            Assert.IsFalse(MoveInputRules.IsFresh(10f, 9.5f));
        }
    }
}