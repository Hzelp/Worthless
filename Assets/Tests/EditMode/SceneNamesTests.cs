using NUnit.Framework;
using Worthless;

namespace Worthless.Tests
{
    public class SceneNamesTests
    {
        [Test]
        public void ThreeScenes_AreDistinctAndNamed()
        {
            Assert.AreEqual("MainMenu", SceneNames.MainMenu);
            Assert.AreEqual("LanLobby", SceneNames.LanLobby);
            Assert.AreEqual("Gameplay", SceneNames.Gameplay);
            Assert.AreNotEqual(SceneNames.MainMenu, SceneNames.LanLobby);
            Assert.AreNotEqual(SceneNames.LanLobby, SceneNames.Gameplay);
            Assert.AreNotEqual(SceneNames.MainMenu, SceneNames.Gameplay);
        }
    }
}