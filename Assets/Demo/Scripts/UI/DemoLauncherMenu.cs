using System.Collections;
using System.IO;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;
using Assets.Demo.Scripts.Core;

namespace Assets.Demo.Scripts.UI
{
  /// <summary>
  /// Starter-scene menu. Put this in a lightweight launcher scene, then load the
  /// gameplay scene only after the player chooses Local or Connected.
  /// </summary>
  public sealed class DemoLauncherMenu : MonoBehaviour
  {
    private enum SetupMode
    {
      Local = 0,
      Connected = 1,
    }

    [SerializeField] private string gameplaySceneName = "CubusGame";
    [SerializeField] private string defaultServerUri = "http://cubus.michaelcuneo.com.au";
    [SerializeField] private string defaultModuleName = "cubus";
    [SerializeField] private string defaultWorldId = "demo_world";
    [SerializeField] private Vector2 panelSize = new(560.0f, 620.0f);

    private SetupMode mode = SetupMode.Connected;
    private string serverUri;
    private string moduleName;
    private string worldId;
    private string seed;
    private string voxelSize = "1";
    private string minChunkY = "-1";
    private string maxChunkY = "2";
    private string minChunkX = "-8";
    private string minChunkZ = "-8";
    private string maxChunkX = "8";
    private string maxChunkZ = "8";
    private TerrainSystem terrainSystem = TerrainSystem.Block;
    private string status = "Choose how to start Cubus.";
    private Vector2 scroll;
    private bool isLoadingGameplayScene;
    private float loadingProgress;
    private float loadingStartedAt;
    private string loadingScenePath;

    private void Awake()
    {
      serverUri = defaultServerUri;
      moduleName = defaultModuleName;
      worldId = defaultWorldId;
      seed = Random.Range(int.MinValue, int.MaxValue).ToString();
      Cursor.lockState = CursorLockMode.None;
      Cursor.visible = true;
    }

    private void OnGUI()
    {
      Rect panelRect = new Rect(
          Mathf.Max(8.0f, (Screen.width - panelSize.x) * 0.5f),
          Mathf.Max(8.0f, (Screen.height - panelSize.y) * 0.5f),
          Mathf.Min(panelSize.x, Screen.width - 16.0f),
          Mathf.Min(panelSize.y, Screen.height - 16.0f));

      GUILayout.BeginArea(panelRect, GUI.skin.window);
      scroll = GUILayout.BeginScrollView(scroll);

      GUILayout.Label("<b>Cubus</b>");
      GUILayout.Label("Create a local world, or connect to a hosted Cubus world.");
      GUILayout.Space(10.0f);

      GUI.enabled = !isLoadingGameplayScene;

      GUILayout.BeginHorizontal();
      if (GUILayout.Toggle(mode == SetupMode.Local, "Local Game", GUI.skin.button))
      {
        mode = SetupMode.Local;
      }
      if (GUILayout.Toggle(mode == SetupMode.Connected, "Connected Game", GUI.skin.button))
      {
        mode = SetupMode.Connected;
      }
      GUILayout.EndHorizontal();

      GUILayout.Space(10.0f);
      DrawWorldFields();

      if (mode == SetupMode.Connected)
      {
        GUILayout.Space(8.0f);
        DrawServerFields();
      }

      GUILayout.Space(12.0f);
      if (GUILayout.Button(mode == SetupMode.Local ? "Start Local Game" : "Start Connected Game", GUILayout.Height(36.0f)))
      {
        StartSelectedGame();
      }

      GUI.enabled = true;

      GUILayout.Space(8.0f);
      GUILayout.Label($"<b>Status:</b> {status}");

      if (isLoadingGameplayScene)
      {
        DrawLoadingStatus();
      }

      GUILayout.EndScrollView();
      GUILayout.EndArea();
    }

    private void DrawLoadingStatus()
    {
      float elapsed = Time.realtimeSinceStartup - loadingStartedAt;
      float progressPercent = Mathf.Clamp01(loadingProgress) * 100.0f;
      GUILayout.Space(12.0f);
      GUILayout.Label("<b>Loading Scene</b>");
      GUILayout.Label($"Scene: {loadingScenePath}");
      GUILayout.Label($"Elapsed: {elapsed:0.0}s");
      GUILayout.Label($"Progress: {progressPercent:0}%");

      Rect progressRect = GUILayoutUtility.GetRect(1.0f, 18.0f, GUILayout.ExpandWidth(true));
      GUI.Box(progressRect, GUIContent.none);
      Rect fill = progressRect;
      fill.width *= Mathf.Clamp01(loadingProgress);
      GUI.Box(fill, GUIContent.none);
      GUILayout.Label("Next stage: CubusGame bootstrap will apply launch settings and prewarm terrain before player control is enabled.");
    }

    private void DrawWorldFields()
    {
      GUILayout.Label("<b>World</b>");
      DrawTextField("World ID", ref worldId);

      GUILayout.BeginHorizontal();
      GUILayout.Label("Terrain", GUILayout.Width(130.0f));
      if (GUILayout.Toggle(terrainSystem == TerrainSystem.Block, "Block", GUI.skin.button))
      {
        terrainSystem = TerrainSystem.Block;
      }
      if (GUILayout.Toggle(terrainSystem == TerrainSystem.SmoothDensity, "Smooth", GUI.skin.button))
      {
        terrainSystem = TerrainSystem.SmoothDensity;
      }
      if (GUILayout.Toggle(terrainSystem == TerrainSystem.Hybrid, "Hybrid", GUI.skin.button))
      {
        terrainSystem = TerrainSystem.Hybrid;
      }
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.Label("Seed", GUILayout.Width(130.0f));
      seed = GUILayout.TextField(seed ?? string.Empty);
      if (GUILayout.Button("Random", GUILayout.Width(90.0f)))
      {
        seed = Random.Range(int.MinValue, int.MaxValue).ToString();
      }
      GUILayout.EndHorizontal();

      DrawTextField("Voxel Size", ref voxelSize);
      DrawTextField("Min Chunk Y", ref minChunkY);
      DrawTextField("Max Chunk Y", ref maxChunkY);
      DrawTextField("Min Chunk X", ref minChunkX);
      DrawTextField("Min Chunk Z", ref minChunkZ);
      DrawTextField("Max Chunk X", ref maxChunkX);
      DrawTextField("Max Chunk Z", ref maxChunkZ);
    }

    private void DrawServerFields()
    {
      GUILayout.Label("<b>Server</b>");
      DrawTextField("Server URI", ref serverUri);
      DrawTextField("Module", ref moduleName);
    }

    private void DrawTextField(string label, ref string value)
    {
      GUILayout.BeginHorizontal();
      GUILayout.Label(label, GUILayout.Width(130.0f));
      value = GUILayout.TextField(value ?? string.Empty);
      GUILayout.EndHorizontal();
    }

    private void StartSelectedGame()
    {
      if (isLoadingGameplayScene)
      {
        return;
      }

      if (!TryBuildLaunchContext())
      {
        return;
      }

      if (string.IsNullOrWhiteSpace(gameplaySceneName))
      {
        status = "Gameplay scene name is empty.";
        return;
      }

      if (!TryFindSceneInBuildSettings(gameplaySceneName.Trim(), out string scenePath))
      {
        status = $"Scene '{gameplaySceneName}' is not in Build Settings. Add it, or set Gameplay Scene Name to the exact scene name.";
        Debug.LogError($"[CubusLauncher] Scene '{gameplaySceneName}' was not found in Build Settings.");
        return;
      }

      StartCoroutine(LoadGameplaySceneRoutine(scenePath));
    }

    private IEnumerator LoadGameplaySceneRoutine(string scenePath)
    {
      isLoadingGameplayScene = true;
      loadingProgress = 0.0f;
      loadingStartedAt = Time.realtimeSinceStartup;
      loadingScenePath = scenePath;
      status = $"Loading scene {scenePath}...";
      Debug.Log($"[CubusLauncher] Loading gameplay scene: {scenePath}");

      AsyncOperation operation = SceneManager.LoadSceneAsync(scenePath);
      if (operation == null)
      {
        status = $"Failed to start loading scene {scenePath}.";
        isLoadingGameplayScene = false;
        yield break;
      }

      operation.allowSceneActivation = true;
      while (!operation.isDone)
      {
        loadingProgress = Mathf.Clamp01(operation.progress / 0.9f);
        status = $"Loading scene {scenePath}: {loadingProgress * 100.0f:0}%";
        yield return null;
      }
    }

    private bool TryBuildLaunchContext()
    {
      if (!int.TryParse(seed, out int parsedSeed))
      {
        status = "Seed must be an integer.";
        return false;
      }

      if (!float.TryParse(voxelSize, out float parsedVoxelSize) || parsedVoxelSize <= 0.0f)
      {
        status = "Voxel Size must be a positive number.";
        return false;
      }

      if (!int.TryParse(minChunkY, out int parsedMinY) || !int.TryParse(maxChunkY, out int parsedMaxY))
      {
        status = "Chunk Y bounds must be integers.";
        return false;
      }

      if (!int.TryParse(minChunkX, out int parsedMinX) || !int.TryParse(minChunkZ, out int parsedMinZ) ||
          !int.TryParse(maxChunkX, out int parsedMaxX) || !int.TryParse(maxChunkZ, out int parsedMaxZ))
      {
        status = "World X/Z bounds must be integers.";
        return false;
      }

      if (parsedMinY > parsedMaxY || parsedMinX > parsedMaxX || parsedMinZ > parsedMaxZ)
      {
        status = "Minimum bounds must be <= maximum bounds.";
        return false;
      }

      if (mode == SetupMode.Local)
      {
        int chunkWidthX = parsedMaxX - parsedMinX + 1;
        int chunkWidthZ = parsedMaxZ - parsedMinZ + 1;
        if (chunkWidthX * chunkWidthZ > 1024)
        {
          status = "Local world bounds are too large for starter generation. Use roughly -8..8 first.";
          return false;
        }
      }

      DemoGameLaunchContext.Set(
          mode == SetupMode.Local ? DemoGameLaunchMode.Local : DemoGameLaunchMode.Connected,
          serverUri,
          moduleName,
          worldId,
          terrainSystem,
          parsedSeed,
          parsedVoxelSize,
          parsedMinY,
          parsedMaxY,
          new Vector2Int(parsedMinX, parsedMinZ),
          new Vector2Int(parsedMaxX, parsedMaxZ));

      return true;
    }

    private static bool TryFindSceneInBuildSettings(string requestedSceneName, out string scenePath)
    {
      scenePath = string.Empty;

      for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
      {
        string path = SceneUtility.GetScenePathByBuildIndex(i);
        string name = Path.GetFileNameWithoutExtension(path);

        if (string.Equals(name, requestedSceneName, System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, requestedSceneName, System.StringComparison.OrdinalIgnoreCase))
        {
          scenePath = path;
          return true;
        }
      }

      return false;
    }
  }
}
