using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEditor;
using UnityEngine;

namespace CubusCore.Editor
{
  [CustomEditor(typeof(CubusWorld))]
  public sealed class CubusWorldEditor : UnityEditor.Editor
  {
    public override void OnInspectorGUI()
    {
      CubusWorld world = (CubusWorld)target;
      WorldStreamer streamer = world.GetComponent<WorldStreamer>();

      WorldSettingsValidationGui.DrawValidationBanner(world, "CubusWorld settings validation");
      DrawGenerationStatus(world);
      DrawGenerationActions(world, streamer);
      DrawDefaultInspector();
    }

    private static void DrawGenerationStatus(CubusWorld world)
    {
      EditorGUILayout.Space(6f);
      EditorGUILayout.LabelField("Generation", EditorStyles.boldLabel);

      EditorGUILayout.HelpBox(
          $"Ready={world.IsWorldReady}, InitialTerrainReady={world.IsInitialTerrainReady}, Progress={(world.GenerationProgress * 100f):0}%",
          MessageType.Info
      );

      if (world.IsInitialTerrainReady)
      {
        EditorGUILayout.HelpBox(
            $"Suggested spawn: {world.SuggestedSpawnLocation}",
            MessageType.None
        );
      }
    }

    private static void DrawGenerationActions(CubusWorld world, WorldStreamer streamer)
    {
      EditorGUILayout.Space(4f);
      EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

      bool hasStreamer = streamer != null;

      if (hasStreamer && !EditorApplication.isPlaying)
      {
        EditorGUILayout.HelpBox(
            "This world uses runtime streaming. Enter Play Mode to generate terrain, render the required chunks, and spawn into the initial terrain.",
            MessageType.Warning
        );
      }

      using (new EditorGUI.DisabledScope(hasStreamer && !EditorApplication.isPlaying))
      {
        if (GUILayout.Button(hasStreamer ? "Regenerate Streamed World" : "Generate World"))
        {
          world.GenerateWorld();
          EditorUtility.SetDirty(world);
        }

        if (GUILayout.Button("Clear World"))
        {
          world.ClearWorld();
          EditorUtility.SetDirty(world);
        }

        if (GUILayout.Button("Clear World And Overrides"))
        {
          world.ClearWorldAndOverrides();
          EditorUtility.SetDirty(world);
        }
      }

      if (hasStreamer)
      {
        EditorGUILayout.HelpBox(
            "Spawn is triggered automatically by the runtime streamer once the initial terrain chunk is ready.",
            MessageType.Info
        );
      }
    }
  }

  [CustomEditor(typeof(WorldStreamer))]
  public sealed class WorldStreamerEditor : UnityEditor.Editor
  {
    public override void OnInspectorGUI()
    {
      WorldStreamer streamer = (WorldStreamer)target;
      CubusWorld world = streamer != null ? streamer.GetComponent<CubusWorld>() : null;

      if (world == null)
      {
        EditorGUILayout.HelpBox(
            "WorldStreamer requires a CubusWorld component on the same GameObject.",
            MessageType.Error
        );
      }
      else
      {
        WorldSettingsValidationGui.DrawValidationBanner(world, "Streaming startup validation");
        DrawStreamingStatus(world);
        DrawStreamingActions(streamer, world);
      }

      DrawDefaultInspector();
    }

    private static void DrawStreamingStatus(CubusWorld world)
    {
      EditorGUILayout.Space(6f);
      EditorGUILayout.LabelField("Runtime Spawn", EditorStyles.boldLabel);

      WorldSettings settings = world.Settings;
      if (settings != null && settings.UseFixedGenerationBounds)
      {
        int estimatedChunks = EstimateFixedBoundsChunkCount(settings);
        long estimatedVoxelSamples = (long)estimatedChunks * 32768L;

        MessageType costMessageType = estimatedChunks > 8192
            ? MessageType.Warning
            : MessageType.Info;

        EditorGUILayout.HelpBox(
            $"Fixed-bounds pre-generation estimate: {estimatedChunks} chunks (~{estimatedVoxelSamples} voxel samples).",
            costMessageType
        );

        if (estimatedChunks > 8192)
        {
          EditorGUILayout.HelpBox(
              "This estimate is large and can make startup very slow. Reduce fixed bounds or let streaming generate on demand.",
              MessageType.Warning
          );
        }
      }

      EditorGUILayout.HelpBox(
          $"WorldReady={world.IsWorldReady}, InitialTerrainReady={world.IsInitialTerrainReady}, Progress={(world.GenerationProgress * 100f):0}%",
          MessageType.Info
      );

      if (world.IsInitialTerrainReady)
      {
        EditorGUILayout.HelpBox(
            $"Suggested spawn: {world.SuggestedSpawnLocation}",
            MessageType.None
        );
      }
      else
      {
        EditorGUILayout.HelpBox(
            "The player will spawn automatically after the streamer finishes building the initial terrain.",
            MessageType.Info
        );
      }
    }

    private static void DrawStreamingActions(WorldStreamer streamer, CubusWorld world)
    {
      EditorGUILayout.Space(4f);
      EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

      if (!EditorApplication.isPlaying)
      {
        EditorGUILayout.HelpBox(
            "Enter Play Mode to generate streamed terrain and spawn into it.",
            MessageType.Warning
        );

        if (GUILayout.Button("Enter Play Mode"))
        {
          EditorApplication.isPlaying = true;
        }

        return;
      }

      if (GUILayout.Button("Regenerate Streamed World"))
      {
        streamer.RegenerateStreamedWorld();
        EditorUtility.SetDirty(world);
      }

      if (GUILayout.Button("Force Refresh Streaming Set"))
      {
        streamer.ForceRefreshStreamingSet();
        EditorUtility.SetDirty(world);
      }
    }

    private static int EstimateFixedBoundsChunkCount(WorldSettings settings)
    {
      int safeRadius = Mathf.Max(0, settings.ViewDistanceInChunks);
      settings.GetGenerationChunkBoundsXZ(
          safeRadius,
          out int minChunkX,
          out int maxChunkX,
          out int minChunkZ,
          out int maxChunkZ
      );

      int xCount = Mathf.Max(0, maxChunkX - minChunkX + 1);
      int zCount = Mathf.Max(0, maxChunkZ - minChunkZ + 1);

      if (settings.TerrainSystem == TerrainSystem.Block)
      {
        settings.GetEffectiveBlockChunkYRange(out int minChunkY, out int maxChunkY);
        int yCount = Mathf.Max(0, maxChunkY - minChunkY + 1);
        return xCount * yCount * zCount;
      }

      settings.GetEffectiveDensityChunkYRange(out int densityMinY, out int densityMaxY);
      int densityYCount = Mathf.Max(0, densityMaxY - densityMinY + 1);
      return xCount * densityYCount * zCount;
    }
  }

  internal static class WorldSettingsValidationGui
  {
    public static void DrawValidationBanner(CubusWorld world, string header)
    {
      if (world == null)
      {
        EditorGUILayout.HelpBox("No CubusWorld found.", MessageType.Error);
        return;
      }

      WorldSettings settings = world.Settings;
      if (settings == null)
      {
        EditorGUILayout.HelpBox("WorldSettings is null.", MessageType.Error);
        return;
      }

      if (settings.TryValidateConfiguration(out string message))
      {
        EditorGUILayout.HelpBox(
            $"{header}: OK for {settings.TerrainSystem} mode.",
            MessageType.Info
        );

        if (settings.TerrainSystem == TerrainSystem.Block)
        {
          EditorGUILayout.HelpBox(
              "Block mode uses hard voxel materials and will show sharp biome boundaries. Use SmoothDensity for visible biome blending.",
              MessageType.Warning
          );
        }
      }
      else
      {
        EditorGUILayout.HelpBox(
            $"{header}: {message}",
            MessageType.Error
        );
      }
    }
  }
}
