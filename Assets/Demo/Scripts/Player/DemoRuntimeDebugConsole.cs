using System;
using System.Collections.Generic;
using System.Globalization;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Assets.Demo.Scripts.Player
{
  [DefaultExecutionOrder(1000)]
  public sealed class DemoRuntimeDebugConsole : MonoBehaviour
  {
    private sealed class DebugCommand
    {
      public string Description;
      public Action<string[]> Handler;

      public DebugCommand(string description, Action<string[]> handler)
      {
        Description = description;
        Handler = handler;
      }
    }

    [SerializeField] private Key toggleOverlayKey = Key.F3;
    [SerializeField] private Key toggleHeatmapKey = Key.F4;
    [SerializeField] private Key toggleConsoleKey = Key.Backquote;
    [SerializeField] private bool overlayVisible = true;
    [SerializeField] private bool heatmapVisible;
    [SerializeField] private int heatmapResolution = 48;
    [SerializeField] private float heatmapWorldSpan = 96.0f;
    [SerializeField] private float heatmapRefreshInterval = 0.25f;

    private CubusWorld world;
    private WorldStreamer streamer;
    private WorldRenderer worldRenderer;
    private WorldPersistence persistence;
    private Camera activeCamera;

    private readonly Queue<string> logLines = new();
    private const int MaxLogLines = 200;
    private readonly List<string> commandHistory = new();
    private int historyIndex = -1;
    private string historyWorkingInput = string.Empty;

    private readonly Dictionary<string, DebugCommand> commands = new(StringComparer.OrdinalIgnoreCase);

    private bool consoleVisible;
    private string commandInput = string.Empty;
    private Vector2 logScroll;

    private bool disabledController;
    private DemoFirstPersonController cachedFirstPersonController;
    private Texture2D heatmapTexture;
    private float heatmapTimeAccumulator;
    private readonly List<string> autocompleteScratch = new();
    private const int ProfilerFrameBufferSize = 512;
    private const float HitchThresholdMs = 33.3f;
    private readonly float[] profilerFrameTimesMs = new float[ProfilerFrameBufferSize];
    private readonly float[] profilerFrameScratchMs = new float[ProfilerFrameBufferSize];
    private int profilerFrameWriteIndex;
    private int profilerFrameCount;
    private int profilerWindowFrameCount;
    private int profilerWindowHitchCount;
    private float profilerWindowElapsed;
    private float profilerWindowFrameTimeTotalMs;
    private float profilerWindowWorstFrameMs;
    private float profilerFps;
    private float profilerAvgFrameMs;
    private float profilerP95FrameMs;
    private float profilerWorstFrameMs;
    private float profilerLoadsPerSecond;
    private float profilerBlockAppliesPerSecond;
    private float profilerDensityAppliesPerSecond;
    private float profilerUnloadsPerSecond;
    private float profilerLoadFailuresPerSecond;
    private bool profilerRealtimeLogEnabled = true;
    private int profilerRealtimeLogSequence;
    private bool profilerCounterBaselineSet;
    private long profilerLastTotalLoads;
    private long profilerLastTotalBlockApplies;
    private long profilerLastTotalDensityApplies;
    private long profilerLastTotalUnloads;
    private long profilerLastTotalLoadFailures;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
      if (FindAnyObjectByType<DemoRuntimeDebugConsole>() != null)
      {
        return;
      }

      GameObject go = new("Demo Runtime Debug Console");
      go.AddComponent<DemoRuntimeDebugConsole>();
      DontDestroyOnLoad(go);
    }

    private void OnEnable()
    {
      Application.logMessageReceived += HandleLogMessage;
      RegisterBuiltInCommands();
    }

    private void OnDisable()
    {
      Application.logMessageReceived -= HandleLogMessage;
    }

    private void OnDestroy()
    {
      if (heatmapTexture != null)
      {
        if (Application.isPlaying)
        {
          Destroy(heatmapTexture);
        }
        else
        {
          DestroyImmediate(heatmapTexture);
        }

        heatmapTexture = null;
      }
    }

    private void Update()
    {
      EnsureReferences();
      UpdateProfilerTelemetry();

      Keyboard keyboard = Keyboard.current;
      if (keyboard == null)
      {
        return;
      }

      if (keyboard[toggleOverlayKey].wasPressedThisFrame)
      {
        overlayVisible = !overlayVisible;
      }

      if (keyboard[toggleHeatmapKey].wasPressedThisFrame)
      {
        heatmapVisible = !heatmapVisible;
      }

      if (keyboard[toggleConsoleKey].wasPressedThisFrame)
      {
        SetConsoleVisible(!consoleVisible);
      }

      if (heatmapVisible)
      {
        heatmapTimeAccumulator += Time.unscaledDeltaTime;
        if (heatmapTimeAccumulator >= Mathf.Max(0.05f, heatmapRefreshInterval))
        {
          heatmapTimeAccumulator = 0.0f;
          RebuildHeatmap();
        }
      }
    }

    private void EnsureReferences()
    {
      if (world == null)
      {
        world = FindAnyObjectByType<CubusWorld>();
      }

      if (streamer == null)
      {
        streamer = FindAnyObjectByType<WorldStreamer>();
      }

      if (worldRenderer == null)
      {
        worldRenderer = FindAnyObjectByType<WorldRenderer>();
      }

      if (persistence == null)
      {
        persistence = FindAnyObjectByType<WorldPersistence>();
      }

      if (activeCamera == null || !activeCamera.isActiveAndEnabled)
      {
        activeCamera = Camera.main;
      }

      if (cachedFirstPersonController == null)
      {
        cachedFirstPersonController = FindAnyObjectByType<DemoFirstPersonController>();
      }
    }

    private void SetConsoleVisible(bool visible)
    {
      consoleVisible = visible;

      if (consoleVisible)
      {
        if (cachedFirstPersonController != null && cachedFirstPersonController.enabled)
        {
          cachedFirstPersonController.enabled = false;
          disabledController = true;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
      }
      else
      {
        if (disabledController && cachedFirstPersonController != null)
        {
          cachedFirstPersonController.enabled = true;
        }

        disabledController = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
      }
    }

    private void OnGUI()
    {
      if (overlayVisible)
      {
        DrawOverlay();
      }

      if (heatmapVisible)
      {
        DrawHeatmapOverlay();
      }

      if (consoleVisible)
      {
        DrawConsole();
      }
    }

    private void DrawOverlay()
    {
      GUIStyle box = new(GUI.skin.box)
      {
        alignment = TextAnchor.UpperLeft,
        fontSize = 14,
        richText = true
      };

      Rect rect = new(12.0f, 12.0f, 720.0f, 255.0f);
      string text = BuildOverlayText();
      GUI.Box(rect, text, box);
    }

    private string BuildOverlayText()
    {
      if (world == null)
      {
        return "<b>Cubus Debug</b>\nWorld: not found\nF3 overlay | F4 heatmap | ` console";
      }

      Vector3Int sampleVoxel;
      bool hasVoxel = TryGetCrosshairVoxel(out sampleVoxel);

      TerrainSample proceduralSample = default;
      ushort storedBlockMaterial = 0;

      if (hasVoxel)
      {
        proceduralSample = world.SampleTerrainAtWorldVoxel(sampleVoxel);
        storedBlockMaterial = world.GetBlockMaterialAtWorldVoxel(sampleVoxel);
      }

      string biomeName = ResolveBiomeName(proceduralSample.BiomeId);

      return
          "<b>Cubus Debug</b>\n" +
          $"Mode: {world.Settings.TerrainSystem} | Ready: {world.IsWorldReady} | InitialReady: {world.IsInitialTerrainReady}\n" +
          $"Camera: {(activeCamera != null ? activeCamera.name : "none")}\n" +
          $"Voxel: {(hasVoxel ? sampleVoxel.ToString() : "n/a")}\n" +
          $"Biome: {proceduralSample.BiomeId} ({biomeName})\n" +
          $"Stored Block Material: {storedBlockMaterial}\n" +
          $"Procedural Material: {proceduralSample.SolidMaterialId}\n" +
          $"Density: {proceduralSample.Density.ToString("0.000", CultureInfo.InvariantCulture)} | SurfaceY: {proceduralSample.SurfaceHeight.ToString("0.00", CultureInfo.InvariantCulture)}\n" +
          BuildProfilerOverlayText() + "\n" +
          "F3 overlay | F4 heatmap | ` console";
    }

    private string BuildProfilerOverlayText()
    {
      int activeChunkViews = worldRenderer != null ? worldRenderer.ActiveChunkViews.Count : -1;
      int desiredChunks = streamer != null ? streamer.DesiredChunkCount : -1;
      int keepChunks = streamer != null ? streamer.KeepChunkCount : -1;
      int pendingLoads = streamer != null ? streamer.PendingLoadCount : -1;
      int pendingRenders = streamer != null ? streamer.PendingRenderCount : -1;
      int pendingUnloads = streamer != null ? streamer.PendingUnloadCount : -1;
      int activeLoadTasks = streamer != null ? streamer.ActiveChunkLoadTaskCount : -1;
      int activeBlockTasks = streamer != null ? streamer.ActiveBlockBuildTaskCount : -1;
      int activeDensityTasks = streamer != null ? streamer.ActiveDensityBuildTaskCount : -1;

      return
          "<b>Profiler</b>\n" +
          $"Frame: {profilerFps.ToString("0.0", CultureInfo.InvariantCulture)} FPS | Avg {profilerAvgFrameMs.ToString("0.00", CultureInfo.InvariantCulture)} ms | " +
          $"P95 {profilerP95FrameMs.ToString("0.00", CultureInfo.InvariantCulture)} ms | Worst {profilerWorstFrameMs.ToString("0.00", CultureInfo.InvariantCulture)} ms | " +
          $"Hitches(>{HitchThresholdMs.ToString("0.0", CultureInfo.InvariantCulture)}ms): {profilerWindowHitchCount}\n" +
          $"Stream/s: Load {profilerLoadsPerSecond.ToString("0.0", CultureInfo.InvariantCulture)} | BlockApply {profilerBlockAppliesPerSecond.ToString("0.0", CultureInfo.InvariantCulture)} | " +
          $"DensityApply {profilerDensityAppliesPerSecond.ToString("0.0", CultureInfo.InvariantCulture)} | Unload {profilerUnloadsPerSecond.ToString("0.0", CultureInfo.InvariantCulture)} | " +
          $"LoadFail {profilerLoadFailuresPerSecond.ToString("0.0", CultureInfo.InvariantCulture)}\n" +
          $"Queues: Desired {desiredChunks} | Keep {keepChunks} | ActiveViews {activeChunkViews} | Pending L/R/U {pendingLoads}/{pendingRenders}/{pendingUnloads} | " +
          $"ActiveTasks L/B/D {activeLoadTasks}/{activeBlockTasks}/{activeDensityTasks}";
    }

    private void UpdateProfilerTelemetry()
    {
      float frameMs = Mathf.Max(0.0f, Time.unscaledDeltaTime * 1000.0f);
      profilerFrameTimesMs[profilerFrameWriteIndex] = frameMs;
      profilerFrameWriteIndex = (profilerFrameWriteIndex + 1) % ProfilerFrameBufferSize;
      if (profilerFrameCount < ProfilerFrameBufferSize)
      {
        profilerFrameCount++;
      }

      profilerWindowFrameCount++;
      profilerWindowElapsed += Time.unscaledDeltaTime;
      profilerWindowFrameTimeTotalMs += frameMs;
      if (frameMs > profilerWindowWorstFrameMs)
      {
        profilerWindowWorstFrameMs = frameMs;
      }

      if (frameMs >= HitchThresholdMs)
      {
        profilerWindowHitchCount++;
      }

      if (profilerWindowElapsed < 1.0f)
      {
        return;
      }

      float elapsed = Mathf.Max(0.0001f, profilerWindowElapsed);
      profilerFps = profilerWindowFrameCount / elapsed;
      profilerAvgFrameMs = profilerWindowFrameCount > 0
          ? profilerWindowFrameTimeTotalMs / profilerWindowFrameCount
          : 0.0f;
      profilerWorstFrameMs = profilerWindowWorstFrameMs;
      profilerP95FrameMs = ComputeP95FrameTimeMs();

      if (streamer != null)
      {
        if (!profilerCounterBaselineSet)
        {
          profilerLastTotalLoads = streamer.TotalChunkLoadsCompleted;
          profilerLastTotalBlockApplies = streamer.TotalBlockMeshApplies;
          profilerLastTotalDensityApplies = streamer.TotalDensityMeshApplies;
          profilerLastTotalUnloads = streamer.TotalChunkUnloadsApplied;
          profilerLastTotalLoadFailures = streamer.TotalChunkLoadFailures;
          profilerCounterBaselineSet = true;
        }

        long totalLoads = streamer.TotalChunkLoadsCompleted;
        long totalBlockApplies = streamer.TotalBlockMeshApplies;
        long totalDensityApplies = streamer.TotalDensityMeshApplies;
        long totalUnloads = streamer.TotalChunkUnloadsApplied;
        long totalLoadFailures = streamer.TotalChunkLoadFailures;

        profilerLoadsPerSecond = Mathf.Max(0.0f, (totalLoads - profilerLastTotalLoads) / elapsed);
        profilerBlockAppliesPerSecond = Mathf.Max(0.0f, (totalBlockApplies - profilerLastTotalBlockApplies) / elapsed);
        profilerDensityAppliesPerSecond = Mathf.Max(0.0f, (totalDensityApplies - profilerLastTotalDensityApplies) / elapsed);
        profilerUnloadsPerSecond = Mathf.Max(0.0f, (totalUnloads - profilerLastTotalUnloads) / elapsed);
        profilerLoadFailuresPerSecond = Mathf.Max(0.0f, (totalLoadFailures - profilerLastTotalLoadFailures) / elapsed);

        profilerLastTotalLoads = totalLoads;
        profilerLastTotalBlockApplies = totalBlockApplies;
        profilerLastTotalDensityApplies = totalDensityApplies;
        profilerLastTotalUnloads = totalUnloads;
        profilerLastTotalLoadFailures = totalLoadFailures;
      }

      EmitRealtimeProfilerLog(elapsed);

      profilerWindowFrameCount = 0;
      profilerWindowElapsed = 0.0f;
      profilerWindowFrameTimeTotalMs = 0.0f;
      profilerWindowWorstFrameMs = 0.0f;
      profilerWindowHitchCount = 0;
    }

    private void EmitRealtimeProfilerLog(float elapsed)
    {
      if (!profilerRealtimeLogEnabled)
      {
        return;
      }

      int activeChunkViews = worldRenderer != null ? worldRenderer.ActiveChunkViews.Count : -1;
      int desiredChunks = streamer != null ? streamer.DesiredChunkCount : -1;
      int keepChunks = streamer != null ? streamer.KeepChunkCount : -1;
      int pendingLoads = streamer != null ? streamer.PendingLoadCount : -1;
      int pendingRenders = streamer != null ? streamer.PendingRenderCount : -1;
      int pendingUnloads = streamer != null ? streamer.PendingUnloadCount : -1;
      int activeLoadTasks = streamer != null ? streamer.ActiveChunkLoadTaskCount : -1;
      int activeBlockTasks = streamer != null ? streamer.ActiveBlockBuildTaskCount : -1;
      int activeDensityTasks = streamer != null ? streamer.ActiveDensityBuildTaskCount : -1;

      profilerRealtimeLogSequence++;
      string line =
          $"CUBUS_PROFILER seq={profilerRealtimeLogSequence} dt={elapsed:0.000} " +
          $"fps={profilerFps:0.0} avgMs={profilerAvgFrameMs:0.00} p95Ms={profilerP95FrameMs:0.00} worstMs={profilerWorstFrameMs:0.00} hitches={profilerWindowHitchCount} " +
          $"loadPerSec={profilerLoadsPerSecond:0.0} blockApplyPerSec={profilerBlockAppliesPerSecond:0.0} densityApplyPerSec={profilerDensityAppliesPerSecond:0.0} " +
          $"unloadPerSec={profilerUnloadsPerSecond:0.0} loadFailPerSec={profilerLoadFailuresPerSecond:0.0} " +
          $"desired={desiredChunks} keep={keepChunks} activeViews={activeChunkViews} pendingL={pendingLoads} pendingR={pendingRenders} pendingU={pendingUnloads} " +
          $"activeTasksL={activeLoadTasks} activeTasksB={activeBlockTasks} activeTasksD={activeDensityTasks}";

      Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", line);
    }

    private float ComputeP95FrameTimeMs()
    {
      if (profilerFrameCount <= 0)
      {
        return 0.0f;
      }

      for (int i = 0; i < profilerFrameCount; i++)
      {
        int sourceIndex = (profilerFrameWriteIndex - profilerFrameCount + i + ProfilerFrameBufferSize) % ProfilerFrameBufferSize;
        profilerFrameScratchMs[i] = profilerFrameTimesMs[sourceIndex];
      }

      Array.Sort(profilerFrameScratchMs, 0, profilerFrameCount);
      int percentileIndex = Mathf.Clamp(Mathf.CeilToInt(profilerFrameCount * 0.95f) - 1, 0, profilerFrameCount - 1);
      return profilerFrameScratchMs[percentileIndex];
    }

    private bool TryGetCrosshairVoxel(out Vector3Int worldVoxel)
    {
      worldVoxel = Vector3Int.zero;

      if (activeCamera == null)
      {
        return false;
      }

      Ray ray = activeCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0.0f));

      if (Physics.Raycast(ray, out RaycastHit hit, 1024.0f, ~0, QueryTriggerInteraction.Ignore))
      {
        if (hit.collider != null && hit.collider.GetComponentInParent<ChunkView>() != null)
        {
          Vector3 p = hit.point + ray.direction.normalized * 0.01f;

          worldVoxel = new Vector3Int(
              Mathf.FloorToInt(p.x),
              Mathf.FloorToInt(p.y),
              Mathf.FloorToInt(p.z)
          );

          return true;
        }
      }

      return false;
    }

    private string ResolveBiomeName(byte biomeId)
    {
      if (world == null || world.Settings == null || world.Settings.BiomeWorldRules == null)
      {
        return "Unknown";
      }

      for (int i = 0; i < world.Settings.BiomeWorldRules.Count; i++)
      {
        BiomeWorldRule rule = world.Settings.BiomeWorldRules[i];
        if (rule?.Biome == null)
        {
          continue;
        }

        byte id = (byte)Mathf.Clamp(rule.Biome.BiomeId, 0, 255);
        if (id == biomeId)
        {
          return string.IsNullOrWhiteSpace(rule.Biome.BiomeName)
              ? rule.Biome.name
              : rule.Biome.BiomeName;
        }
      }

      return "Unknown";
    }

    private void DrawConsole()
    {
      float width = Mathf.Min(Screen.width - 24.0f, 920.0f);
      float height = Mathf.Min(Screen.height - 24.0f, 460.0f);
      Rect rect = new(12.0f, Screen.height - height - 12.0f, width, height);

      GUI.Box(rect, "");

      GUILayout.BeginArea(new Rect(rect.x + 8.0f, rect.y + 8.0f, rect.width - 16.0f, rect.height - 16.0f));
      GUILayout.Label("Runtime Console (` to close)");

      logScroll = GUILayout.BeginScrollView(logScroll, GUILayout.Height(rect.height - 80.0f));
      foreach (string line in logLines)
      {
        GUILayout.Label(line);
      }
      GUILayout.EndScrollView();

      GUI.SetNextControlName("DebugConsoleInput");
      commandInput = GUILayout.TextField(commandInput);

      Event e = Event.current;
      if (e.type == EventType.KeyDown)
      {
        if (e.keyCode == KeyCode.Return)
        {
          ExecuteCommand(commandInput);
          commandInput = string.Empty;
          historyIndex = -1;
          historyWorkingInput = string.Empty;
          e.Use();
        }
        else if (e.keyCode == KeyCode.UpArrow)
        {
          NavigateHistory(older: true);
          e.Use();
        }
        else if (e.keyCode == KeyCode.DownArrow)
        {
          NavigateHistory(older: false);
          e.Use();
        }
        else if (e.keyCode == KeyCode.Tab)
        {
          ApplyAutocomplete();
          e.Use();
        }
      }

      GUI.FocusControl("DebugConsoleInput");
      GUILayout.EndArea();
    }

    private void DrawHeatmapOverlay()
    {
      if (heatmapTexture == null)
      {
        RebuildHeatmap();
      }

      if (heatmapTexture == null)
      {
        return;
      }

      float size = Mathf.Min(Screen.width * 0.28f, Screen.height * 0.28f);
      Rect panelRect = new(Screen.width - size - 14.0f, 14.0f, size, size + 22.0f);
      GUI.Box(panelRect, "");

      Rect textureRect = new(panelRect.x + 6.0f, panelRect.y + 18.0f, panelRect.width - 12.0f, panelRect.width - 12.0f);
      GUI.DrawTexture(textureRect, heatmapTexture, ScaleMode.StretchToFill, false);

      float cx = textureRect.x + textureRect.width * 0.5f;
      float cy = textureRect.y + textureRect.height * 0.5f;
      Color prev = GUI.color;
      GUI.color = new Color(1.0f, 1.0f, 1.0f, 0.85f);
      GUI.DrawTexture(new Rect(cx - 1.0f, textureRect.y, 2.0f, textureRect.height), Texture2D.whiteTexture);
      GUI.DrawTexture(new Rect(textureRect.x, cy - 1.0f, textureRect.width, 2.0f), Texture2D.whiteTexture);
      GUI.color = prev;

      GUI.Label(
          new Rect(panelRect.x + 8.0f, panelRect.y + 2.0f, panelRect.width - 16.0f, 18.0f),
          $"Biome Heatmap ({Mathf.RoundToInt(heatmapWorldSpan)}m)"
      );
    }

    private void RebuildHeatmap()
    {
      if (world == null || world.Settings == null || activeCamera == null)
      {
        return;
      }

      int resolution = Mathf.Clamp(heatmapResolution, 16, 128);
      if (heatmapTexture == null || heatmapTexture.width != resolution || heatmapTexture.height != resolution)
      {
        if (heatmapTexture != null)
        {
          if (Application.isPlaying)
          {
            Destroy(heatmapTexture);
          }
          else
          {
            DestroyImmediate(heatmapTexture);
          }
        }

        heatmapTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
          name = "Cubus Biome Heatmap",
          wrapMode = TextureWrapMode.Clamp,
          filterMode = FilterMode.Point
        };
      }

      Vector3 center = activeCamera.transform.position;
      float span = Mathf.Max(8.0f, heatmapWorldSpan);

      for (int y = 0; y < resolution; y++)
      {
        float nz = (y + 0.5f) / resolution - 0.5f;

        for (int x = 0; x < resolution; x++)
        {
          float nx = (x + 0.5f) / resolution - 0.5f;
          float worldX = center.x + nx * span;
          float worldZ = center.z + nz * span;

          world.Settings.ResolveBiomeAtWorldXZ(worldX, worldZ, out _, out byte biomeId);
          heatmapTexture.SetPixel(x, y, GetBiomeDebugColor(biomeId));
        }
      }

      heatmapTexture.Apply(false, false);
    }

    private static Color GetBiomeDebugColor(byte biomeId)
    {
      float hue = (biomeId % 64) / 64.0f;
      Color baseColor = Color.HSVToRGB(hue, 0.8f, 0.95f);
      return new Color(baseColor.r, baseColor.g, baseColor.b, 0.95f);
    }

    public bool RegisterCommand(string commandName, string description, Action<string[]> handler)
    {
      if (string.IsNullOrWhiteSpace(commandName) || handler == null)
      {
        return false;
      }

      string key = commandName.Trim().ToLowerInvariant();
      if (commands.ContainsKey(key))
      {
        return false;
      }

      commands.Add(key, new DebugCommand(description ?? string.Empty, handler));
      return true;
    }

    public bool UnregisterCommand(string commandName)
    {
      if (string.IsNullOrWhiteSpace(commandName))
      {
        return false;
      }

      return commands.Remove(commandName.Trim().ToLowerInvariant());
    }

    private void RegisterBuiltInCommands()
    {
      if (commands.Count > 0)
      {
        return;
      }

      RegisterCommand("help", "List available commands.", _ =>
      {
        List<string> names = new(commands.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        EnqueueLog($"Commands ({names.Count}):");
        for (int i = 0; i < names.Count; i++)
        {
          string key = names[i];
          if (commands.TryGetValue(key, out DebugCommand command))
          {
            EnqueueLog($"- {key}: {command.Description}");
          }
        }
      });

      RegisterCommand("clear", "Clear console log output.", _ =>
      {
        logLines.Clear();
      });

      RegisterCommand("biome", "Print biome/material info at crosshair.", _ =>
      {
        if (world == null)
        {
          EnqueueLog("No CubusWorld found.");
          return;
        }

        if (!TryGetCrosshairVoxel(out Vector3Int voxel))
        {
          EnqueueLog("No block hit under crosshair.");
          return;
        }

        TerrainSample s = world.SampleTerrainAtWorldVoxel(voxel);
        ushort storedMaterial = world.GetBlockMaterialAtWorldVoxel(voxel);

        EnqueueLog(
            $"Voxel={voxel}, " +
            $"Biome={s.BiomeId} ({ResolveBiomeName(s.BiomeId)}), " +
            $"StoredBlockMat={storedMaterial}, " +
            $"ProceduralMat={s.SolidMaterialId}, " +
            $"Density={s.Density:0.000}, " +
            $"SurfaceY={s.SurfaceHeight:0.00}"
        );
      });

      RegisterCommand("regen", "Generate world immediately.", _ =>
      {
        if (world == null)
        {
          EnqueueLog("No CubusWorld found.");
          return;
        }

        world.GenerateWorld();
        EnqueueLog("Regenerate requested.");
      });

      RegisterCommand("save", "Save default world.", _ =>
      {
        if (persistence == null)
        {
          EnqueueLog("No WorldPersistence found.");
          return;
        }

        EnqueueLog(persistence.SaveDefaultWorld() ? "Save OK" : "Save failed");
      });

      RegisterCommand("load", "Load default world.", _ =>
      {
        if (persistence == null)
        {
          EnqueueLog("No WorldPersistence found.");
          return;
        }

        EnqueueLog(persistence.LoadDefaultWorld() ? "Load OK" : "Load failed");
      });

      RegisterCommand("clear_saves", "Clear all world save files.", _ =>
      {
        if (persistence == null)
        {
          EnqueueLog("No WorldPersistence found.");
          return;
        }

        EnqueueLog(persistence.ClearAllWorldSaves() ? "Cleared saves" : "No saves cleared");
      });

      RegisterCommand("mode", "Set terrain mode: mode block|density", args =>
      {
        if (world == null)
        {
          EnqueueLog("No CubusWorld found.");
          return;
        }

        if (args.Length < 2)
        {
          EnqueueLog("Usage: mode block|density");
          return;
        }

        if (args[1].Equals("block", StringComparison.OrdinalIgnoreCase))
        {
          world.Settings.TerrainSystem = TerrainSystem.Block;
          EnqueueLog("Mode set to Block.");
        }
        else if (args[1].Equals("density", StringComparison.OrdinalIgnoreCase))
        {
          world.Settings.TerrainSystem = TerrainSystem.SmoothDensity;
          EnqueueLog("Mode set to SmoothDensity.");
        }
        else
        {
          EnqueueLog("Usage: mode block|density");
        }
      });

      RegisterCommand("heatmap", "Toggle heatmap: heatmap on|off|toggle", args =>
      {
        if (args.Length < 2 || args[1].Equals("toggle", StringComparison.OrdinalIgnoreCase))
        {
          heatmapVisible = !heatmapVisible;
        }
        else if (args[1].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
          heatmapVisible = true;
        }
        else if (args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
          heatmapVisible = false;
        }
        else
        {
          EnqueueLog("Usage: heatmap on|off|toggle");
          return;
        }

        if (heatmapVisible)
        {
          RebuildHeatmap();
        }

        EnqueueLog($"Heatmap {(heatmapVisible ? "enabled" : "disabled")}");
      });

      RegisterCommand("overlay", "Toggle info overlay: overlay on|off|toggle", args =>
      {
        if (args.Length < 2 || args[1].Equals("toggle", StringComparison.OrdinalIgnoreCase))
        {
          overlayVisible = !overlayVisible;
        }
        else if (args[1].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
          overlayVisible = true;
        }
        else if (args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
          overlayVisible = false;
        }
        else
        {
          EnqueueLog("Usage: overlay on|off|toggle");
          return;
        }

        EnqueueLog($"Overlay {(overlayVisible ? "enabled" : "disabled")}");
      });

      RegisterCommand("profiler", "Profiler controls: profiler status|reset|log on|off", args =>
      {
        if (args.Length >= 2 && args[1].Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
          profilerCounterBaselineSet = false;
          profilerLoadsPerSecond = 0.0f;
          profilerBlockAppliesPerSecond = 0.0f;
          profilerDensityAppliesPerSecond = 0.0f;
          profilerUnloadsPerSecond = 0.0f;
          profilerLoadFailuresPerSecond = 0.0f;
          profilerRealtimeLogSequence = 0;
          EnqueueLog("Profiler counters reset.");
          return;
        }

        if (args.Length >= 3 && args[1].Equals("log", StringComparison.OrdinalIgnoreCase))
        {
          if (args[2].Equals("on", StringComparison.OrdinalIgnoreCase))
          {
            profilerRealtimeLogEnabled = true;
            EnqueueLog("Realtime profiler logging enabled.");
            return;
          }

          if (args[2].Equals("off", StringComparison.OrdinalIgnoreCase))
          {
            profilerRealtimeLogEnabled = false;
            EnqueueLog("Realtime profiler logging disabled.");
            return;
          }

          EnqueueLog("Usage: profiler log on|off");
          return;
        }

        EnqueueLog(
            $"Profiler: FPS={profilerFps:0.0}, AvgMs={profilerAvgFrameMs:0.00}, P95Ms={profilerP95FrameMs:0.00}, WorstMs={profilerWorstFrameMs:0.00}, " +
            $"Load/s={profilerLoadsPerSecond:0.0}, BlockApply/s={profilerBlockAppliesPerSecond:0.0}, DensityApply/s={profilerDensityAppliesPerSecond:0.0}, " +
            $"Unload/s={profilerUnloadsPerSecond:0.0}, LoadFail/s={profilerLoadFailuresPerSecond:0.0}, Log={(profilerRealtimeLogEnabled ? "on" : "off")}"
        );
      });
    }

    private void ExecuteCommand(string raw)
    {
      string input = (raw ?? string.Empty).Trim();
      if (string.IsNullOrWhiteSpace(input))
      {
        return;
      }

      EnqueueLog($"> {input}");

      if (commandHistory.Count == 0 || !string.Equals(commandHistory[commandHistory.Count - 1], input, StringComparison.Ordinal))
      {
        commandHistory.Add(input);
      }

      string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      string commandKey = parts[0].ToLowerInvariant();

      if (commands.TryGetValue(commandKey, out DebugCommand cmd))
      {
        try
        {
          cmd.Handler(parts);
        }
        catch (Exception ex)
        {
          EnqueueLog($"Command '{commandKey}' failed: {ex.Message}");
        }
      }
      else
      {
        autocompleteScratch.Clear();
        CollectAutocompleteMatches(commandKey, autocompleteScratch);
        if (autocompleteScratch.Count > 0)
        {
          EnqueueLog($"Unknown command: {commandKey}. Did you mean: {string.Join(", ", autocompleteScratch)}");
        }
        else
        {
          EnqueueLog($"Unknown command: {commandKey}");
        }
      }
    }

    private void NavigateHistory(bool older)
    {
      if (commandHistory.Count == 0)
      {
        return;
      }

      if (older)
      {
        if (historyIndex < 0)
        {
          historyWorkingInput = commandInput;
          historyIndex = commandHistory.Count - 1;
        }
        else if (historyIndex > 0)
        {
          historyIndex--;
        }
      }
      else
      {
        if (historyIndex < 0)
        {
          return;
        }

        if (historyIndex < commandHistory.Count - 1)
        {
          historyIndex++;
        }
        else
        {
          historyIndex = -1;
          commandInput = historyWorkingInput;
          return;
        }
      }

      commandInput = commandHistory[historyIndex];
    }

    private void ApplyAutocomplete()
    {
      string raw = commandInput ?? string.Empty;
      int firstSpace = raw.IndexOf(' ');
      if (firstSpace >= 0)
      {
        return;
      }

      string prefix = raw.Trim().ToLowerInvariant();
      if (prefix.Length == 0)
      {
        return;
      }

      autocompleteScratch.Clear();
      CollectAutocompleteMatches(prefix, autocompleteScratch);

      if (autocompleteScratch.Count == 0)
      {
        return;
      }

      if (autocompleteScratch.Count == 1)
      {
        commandInput = autocompleteScratch[0] + " ";
        return;
      }

      EnqueueLog($"Suggestions: {string.Join(", ", autocompleteScratch)}");
      commandInput = autocompleteScratch[0];
    }

    private void CollectAutocompleteMatches(string prefix, List<string> destination)
    {
      destination.Clear();
      foreach (string key in commands.Keys)
      {
        if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
          destination.Add(key);
        }
      }

      destination.Sort(StringComparer.OrdinalIgnoreCase);
      if (destination.Count > 6)
      {
        destination.RemoveRange(6, destination.Count - 6);
      }
    }

    private void HandleLogMessage(string condition, string stackTrace, LogType type)
    {
      string prefix = type switch
      {
        LogType.Error => "[Error]",
        LogType.Assert => "[Assert]",
        LogType.Warning => "[Warn]",
        LogType.Exception => "[Exception]",
        _ => "[Log]"
      };

      EnqueueLog($"{prefix} {condition}");
    }

    private void EnqueueLog(string text)
    {
      logLines.Enqueue(text);
      while (logLines.Count > MaxLogLines)
      {
        logLines.Dequeue();
      }

      logScroll.y = float.MaxValue;
    }
  }
}
