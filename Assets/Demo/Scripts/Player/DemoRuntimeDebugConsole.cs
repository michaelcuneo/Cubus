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

    [SerializeField] private Key printDebugSnapshotKey = Key.F3;
    [SerializeField] private Key toggleConsoleKey = Key.Backquote;
    [SerializeField] private bool consoleVisible;
    [SerializeField] private bool profilerRealtimeLogEnabled = true;
    [SerializeField] private int maxLogLines = 300;

    private CubusWorld world;
    private WorldStreamer streamer;
    private WorldRenderer worldRenderer;
    private WorldPersistence persistence;
    private Camera activeCamera;

    private readonly Queue<string> logLines = new();
    private readonly List<string> commandHistory = new();
    private readonly List<string> autocompleteScratch = new();
    private readonly Dictionary<string, DebugCommand> commands = new(StringComparer.OrdinalIgnoreCase);

    private string commandInput = string.Empty;
    private string historyWorkingInput = string.Empty;
    private int historyIndex = -1;
    private Vector2 logScroll;
    private bool disabledController;
    private DemoFirstPersonController cachedFirstPersonController;

    private const int ProfilerFrameBufferSize = 512;
    private const float HitchThresholdMs = 33.3f;
    private readonly float[] profilerFrameTimesMs = new float[ProfilerFrameBufferSize];
    private readonly float[] profilerFrameScratchMs = new float[ProfilerFrameBufferSize];
    private int profilerFrameWriteIndex;
    private int profilerFrameCount;
    private int profilerWindowFrameCount;
    private int profilerWindowHitchCount;
    private int profilerRealtimeLogSequence;
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

    private void Update()
    {
      EnsureReferences();
      UpdateProfilerTelemetry();

      Keyboard keyboard = Keyboard.current;
      if (keyboard == null)
      {
        return;
      }

      if (keyboard[printDebugSnapshotKey].wasPressedThisFrame)
      {
        PrintDebugSnapshot("F3 debug snapshot");
      }

      if (keyboard[toggleConsoleKey].wasPressedThisFrame)
      {
        SetConsoleVisible(!consoleVisible);
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
      if (consoleVisible)
      {
        DrawConsole();
      }
    }

    private void DrawConsole()
    {
      float width = Mathf.Min(Screen.width - 24.0f, 980.0f);
      float height = Mathf.Min(Screen.height - 24.0f, 520.0f);
      Rect rect = new(12.0f, Screen.height - height - 12.0f, width, height);

      GUI.Box(rect, string.Empty);
      GUILayout.BeginArea(new Rect(rect.x + 8.0f, rect.y + 8.0f, rect.width - 16.0f, rect.height - 16.0f));
      GUILayout.Label("Runtime Console (` to close, F3 debug snapshot, command: dev on)");

      logScroll = GUILayout.BeginScrollView(logScroll, GUILayout.Height(rect.height - 82.0f));
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
      profilerWindowWorstFrameMs = Mathf.Max(profilerWindowWorstFrameMs, frameMs);
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
      profilerAvgFrameMs = profilerWindowFrameCount > 0 ? profilerWindowFrameTimeTotalMs / profilerWindowFrameCount : 0.0f;
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

      Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}",
          $"CUBUS_PROFILER seq={++profilerRealtimeLogSequence} dt={elapsed:0.000} " + BuildProfilerSummary());
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

    private string BuildProfilerSummary()
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
          $"fps={profilerFps:0.0} avgMs={profilerAvgFrameMs:0.00} p95Ms={profilerP95FrameMs:0.00} worstMs={profilerWorstFrameMs:0.00} hitches={profilerWindowHitchCount} " +
          $"loadPerSec={profilerLoadsPerSecond:0.0} blockApplyPerSec={profilerBlockAppliesPerSecond:0.0} densityApplyPerSec={profilerDensityAppliesPerSecond:0.0} " +
          $"unloadPerSec={profilerUnloadsPerSecond:0.0} loadFailPerSec={profilerLoadFailuresPerSecond:0.0} " +
          $"desired={desiredChunks} keep={keepChunks} activeViews={activeChunkViews} pendingL={pendingLoads} pendingR={pendingRenders} pendingU={pendingUnloads} " +
          $"activeTasksL={activeLoadTasks} activeTasksB={activeBlockTasks} activeTasksD={activeDensityTasks}";
    }

    private void PrintDebugSnapshot(string reason)
    {
      string line = $"CUBUS_DEBUG {reason}: {BuildDebugSnapshot()}";
      EnqueueLog(line);
      Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", line);
    }

    private string BuildDebugSnapshot()
    {
      string worldSummary = world == null
          ? "world=none"
          : $"mode={world.Settings.TerrainSystem} worldReady={world.IsWorldReady} initialReady={world.IsInitialTerrainReady} generationProgress={world.GenerationProgress:0.00} status='{world.GenerationStatus}' spawn={world.SuggestedSpawnLocation}";

      return $"{worldSummary}; {BuildCrosshairSummary()}; {BuildProfilerSummary()}; devMode={Assets.Demo.Scripts.Multiplayer.CubusDeveloperMode.IsEnabled}";
    }

    private string BuildCrosshairSummary()
    {
      if (world == null)
      {
        return "crosshair=no-world";
      }

      if (!TryGetCrosshairVoxel(out Vector3Int voxel))
      {
        return "crosshair=no-hit";
      }

      TerrainSample sample = world.SampleTerrainAtWorldVoxel(voxel);
      ushort storedMaterial = world.GetBlockMaterialAtWorldVoxel(voxel);
      return
          $"voxel={voxel} biome={sample.BiomeId}({ResolveBiomeName(sample.BiomeId)}) " +
          $"storedBlockMaterial={storedMaterial} proceduralMaterial={sample.SolidMaterialId} " +
          $"density={sample.Density.ToString("0.000", CultureInfo.InvariantCulture)} surfaceY={sample.SurfaceHeight.ToString("0.00", CultureInfo.InvariantCulture)}";
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
          worldVoxel = new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
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
          return string.IsNullOrWhiteSpace(rule.Biome.BiomeName) ? rule.Biome.name : rule.Biome.BiomeName;
        }
      }

      return "Unknown";
    }

    private bool RegisterCommand(string commandName, string description, Action<string[]> handler)
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

      RegisterCommand("clear", "Clear console log output.", _ => logLines.Clear());
      RegisterCommand("debug", "Print Cubus debug snapshot to the console.", _ => PrintDebugSnapshot("command"));
      RegisterCommand("biome", "Print biome/material info at crosshair.", _ => EnqueueLog(BuildCrosshairSummary()));

      RegisterCommand("dev", "Developer mode controls: dev on|off|status", args =>
      {
        if (args.Length < 2 || args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
          EnqueueLog($"Developer mode is {(Assets.Demo.Scripts.Multiplayer.CubusDeveloperMode.IsEnabled ? "ON" : "OFF")}.");
          return;
        }

        if (args[1].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
          Assets.Demo.Scripts.Multiplayer.CubusDeveloperMode.IsEnabled = true;
          EnqueueLog("Developer mode enabled. Launcher dev world tools are now visible.");
          return;
        }

        if (args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
          Assets.Demo.Scripts.Multiplayer.CubusDeveloperMode.IsEnabled = false;
          EnqueueLog("Developer mode disabled. Launcher dev world tools are hidden.");
          return;
        }

        EnqueueLog("Usage: dev on|off|status");
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

        EnqueueLog($"Profiler: {BuildProfilerSummary()}, Log={(profilerRealtimeLogEnabled ? "on" : "off")}");
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
        EnqueueLog(autocompleteScratch.Count > 0
            ? $"Unknown command: {commandKey}. Did you mean: {string.Join(", ", autocompleteScratch)}"
            : $"Unknown command: {commandKey}");
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
      int limit = Mathf.Max(50, maxLogLines);
      while (logLines.Count > limit)
      {
        logLines.Dequeue();
      }

      logScroll.y = float.MaxValue;
    }
  }
}
