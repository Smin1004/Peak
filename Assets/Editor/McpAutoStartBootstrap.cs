using UnityEditor;
using UnityEngine;

namespace Peak.EditorTools
{
    /// <summary>
    /// One-time bootstrap for MCP for Unity (com.coplaydev.unity-mcp).
    /// Enables the HTTP transport and "Auto-Start on Editor Load" so the local MCP server
    /// (http://127.0.0.1:8080) and the Unity bridge come up automatically, without clicking
    /// "Start Server" in Window > MCP for Unity. The settings persist in EditorPrefs, so this
    /// file can be deleted once the connection has been verified.
    /// </summary>
    [InitializeOnLoad]
    internal static class McpAutoStartBootstrap
    {
        private const string BootstrappedKey = "MCPForUnity.Peak.AutoStartBootstrapped";
        private const string ReloadRequestedKey = "MCPForUnity.Peak.AutoStartReloadRequested";

        static McpAutoStartBootstrap()
        {
            if (Application.isBatchMode) return;
            if (EditorPrefs.GetBool(BootstrappedKey, false)) return;

            EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", true);
            EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", true);
            EditorPrefs.SetBool(BootstrappedKey, true);
            Debug.Log("[MCP Bootstrap] Enabled MCP for Unity HTTP transport + auto-start on editor load.");

            // The package reads AutoStartOnLoad inside its own [InitializeOnLoad] constructor and
            // cross-assembly load order is not guaranteed, so request one extra domain reload.
            if (!SessionState.GetBool(ReloadRequestedKey, false))
            {
                SessionState.SetBool(ReloadRequestedKey, true);
                EditorApplication.delayCall += EditorUtility.RequestScriptReload;
            }
        }
    }
}
