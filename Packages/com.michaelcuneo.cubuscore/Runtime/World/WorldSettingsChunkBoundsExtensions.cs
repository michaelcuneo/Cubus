using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  public static class WorldSettingsChunkBoundsExtensions
  {
    private const int CompatibilityWorldMinChunkY = int.MinValue / 4;
    private const int CompatibilityWorldMaxChunkY = int.MaxValue / 4;
    private const int MinimumRuntimeWorldRadiusInChunks = 1024;

    public static ChunkBounds3D GetEffectiveGenerationChunkBounds3D(this WorldSettings settings)
    {
      if (settings == null)
      {
        return new ChunkBounds3D(Vector3Int.zero, Vector3Int.zero);
      }

      settings.GetGenerationChunkBounds3D(
        out int minX,
        out int maxX,
        out int minY,
        out int maxY,
        out int minZ,
        out int maxZ
      );

      return new ChunkBounds3D(
        new Vector3Int(minX, minY, minZ),
        new Vector3Int(maxX, maxY, maxZ)
      );
    }

    public static ChunkBounds3D GetEffectiveWorldChunkBounds3D(this WorldSettings settings)
    {
      if (settings == null)
      {
        return new ChunkBounds3D(Vector3Int.zero, Vector3Int.zero);
      }

      int minX = Mathf.Min(settings.WorldMinChunkXZ.x, settings.WorldMaxChunkXZ.x);
      int maxX = Mathf.Max(settings.WorldMinChunkXZ.x, settings.WorldMaxChunkXZ.x);
      int minZ = Mathf.Min(settings.WorldMinChunkXZ.y, settings.WorldMaxChunkXZ.y);
      int maxZ = Mathf.Max(settings.WorldMinChunkXZ.y, settings.WorldMaxChunkXZ.y);

      return new ChunkBounds3D(
        new Vector3Int(minX, CompatibilityWorldMinChunkY, minZ),
        new Vector3Int(maxX, CompatibilityWorldMaxChunkY, maxZ)
      );
    }

    public static bool IsInsideEffectiveWorldBounds3D(this WorldSettings settings, Vector3Int chunkCoord)
    {
      if (settings == null)
      {
        return false;
      }

      return !settings.UseWorldBounds || settings.GetEffectiveWorldChunkBounds3D().Contains(chunkCoord);
    }

    public static bool IsInsideEffectiveGenerationBounds3D(this WorldSettings settings, Vector3Int chunkCoord)
    {
      if (settings == null)
      {
        return false;
      }

      return !settings.UseFixedGenerationBounds || settings.GetEffectiveGenerationChunkBounds3D().Contains(chunkCoord);
    }

    public static void EnsureRuntimeStreamingBounds(this WorldSettings settings)
    {
      if (settings == null)
      {
        return;
      }

      // Runtime streaming must not inherit small pregeneration/test bounds such as
      // -4..4 or -8..8. Those are only useful for tiny offline pregeneration passes;
      // gameplay streaming expects a wide world and should generate missing chunks
      // procedurally as the player moves.
      settings.UseWorldBounds = true;

      int minX = Mathf.Min(settings.WorldMinChunkXZ.x, settings.WorldMaxChunkXZ.x);
      int maxX = Mathf.Max(settings.WorldMinChunkXZ.x, settings.WorldMaxChunkXZ.x);
      int minZ = Mathf.Min(settings.WorldMinChunkXZ.y, settings.WorldMaxChunkXZ.y);
      int maxZ = Mathf.Max(settings.WorldMinChunkXZ.y, settings.WorldMaxChunkXZ.y);

      if (minX > -MinimumRuntimeWorldRadiusInChunks) minX = -MinimumRuntimeWorldRadiusInChunks;
      if (maxX < MinimumRuntimeWorldRadiusInChunks) maxX = MinimumRuntimeWorldRadiusInChunks;
      if (minZ > -MinimumRuntimeWorldRadiusInChunks) minZ = -MinimumRuntimeWorldRadiusInChunks;
      if (maxZ < MinimumRuntimeWorldRadiusInChunks) maxZ = MinimumRuntimeWorldRadiusInChunks;

      settings.WorldMinChunkXZ = new Vector2Int(minX, minZ);
      settings.WorldMaxChunkXZ = new Vector2Int(maxX, maxZ);

      settings.UseFixedGenerationBounds = false;
      settings.MissingChunkPolicy = MissingChunkPolicy.GenerateLocally;
    }

    public static void GetEffectiveGenerationChunkBounds3D(
      this WorldSettings settings,
      out int minChunkX,
      out int maxChunkX,
      out int minChunkY,
      out int maxChunkY,
      out int minChunkZ,
      out int maxChunkZ)
    {
      ChunkBounds3D bounds = settings.GetEffectiveGenerationChunkBounds3D();
      bounds.GetBounds(out minChunkX, out maxChunkX, out minChunkY, out maxChunkY, out minChunkZ, out maxChunkZ);
    }

    public static void GetEffectiveWorldChunkBounds3D(
      this WorldSettings settings,
      out int minChunkX,
      out int maxChunkX,
      out int minChunkY,
      out int maxChunkY,
      out int minChunkZ,
      out int maxChunkZ)
    {
      ChunkBounds3D bounds = settings.GetEffectiveWorldChunkBounds3D();
      bounds.GetBounds(out minChunkX, out maxChunkX, out minChunkY, out maxChunkY, out minChunkZ, out maxChunkZ);
    }
  }
}
