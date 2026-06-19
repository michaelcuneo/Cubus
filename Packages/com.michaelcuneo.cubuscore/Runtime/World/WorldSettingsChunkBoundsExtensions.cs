using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  public static class WorldSettingsChunkBoundsExtensions
  {
    private const int DefaultGeneratedMinChunkY = -16;
    private const int DefaultGeneratedMaxChunkY = 16;
    private const int CompatibilityWorldMinChunkY = int.MinValue / 4;
    private const int CompatibilityWorldMaxChunkY = int.MaxValue / 4;

    public static ChunkBounds3D GetEffectiveGenerationChunkBounds3D(this WorldSettings settings)
    {
      if (settings == null)
      {
        return new ChunkBounds3D(Vector3Int.zero, Vector3Int.zero);
      }

      settings.GetGenerationChunkBoundsXZ(
        out int minX,
        out int maxX,
        out int minZ,
        out int maxZ
      );

      return new ChunkBounds3D(
        new Vector3Int(minX, DefaultGeneratedMinChunkY, minZ),
        new Vector3Int(maxX, DefaultGeneratedMaxChunkY, maxZ)
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
