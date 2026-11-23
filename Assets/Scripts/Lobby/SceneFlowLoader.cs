using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Worthless
{
    /// <summary>离场/回主菜单的加载器：先拆网络对象，再单场景加载，避免重复 NetworkManager 与残留 Canvas。</summary>
    public static class SceneFlowLoader
    {
        public static void LoadAfterNetworkTeardown(NetworkManager manager, string sceneName)
        {
            TransitionOverlay.LoadScene(sceneName, Teardown(manager));
        }

        private static IEnumerator Teardown(NetworkManager manager)
        {
            if (manager != null)
            {
                if (manager.IsListening || manager.ShutdownInProgress) manager.Shutdown();
                var waited = 0f;
                while (manager != null && (manager.IsListening || manager.ShutdownInProgress) && waited < 3f)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (manager != null) Object.Destroy(manager.gameObject);
            }

            yield return null;
        }
    }
}
