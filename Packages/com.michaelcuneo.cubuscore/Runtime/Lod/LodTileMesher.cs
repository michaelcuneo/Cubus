using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod
{
  /// <summary>
  /// Meshes a Distant-Horizon LOD tile by running its downsampled
  /// <see cref="BlockChunkData"/> (from <see cref="LodChunkBuilder"/>) through the
  /// existing <see cref="BlockGreedyMesher"/> at the level's coarse voxel size.
  /// The neighbour lookup is the coarse terrain classifier
  /// (<see cref="LodChunkBuilder.CreateMaterialLookup"/>), so the single-sided
  /// mesher culls faces shared with adjacent same-level tiles and only the outer
  /// surface is emitted - distant terrain reuses the biome-atlas material verbatim.
  ///
  /// LOD tiles are a pure function of the world snapshot, so they never need
  /// re-meshing when neighbours stream in (unlike full chunks).
  /// </summary>
  public static class LodTileMesher
  {
    /// <summary>
    /// Builds and meshes a tile in one call. Returns <c>null</c> when the tile has
    /// no solid cells (caller should skip it). The returned <see cref="MeshData"/>
    /// is freshly allocated by the mesher. When <paramref name="skirtCells"/> &gt; 0
    /// and the tile has visible surface, downward perimeter skirts are appended to
    /// hide cracks at band boundaries.
    /// </summary>
    public static MeshData BuildAndMesh(
        WorldGenerationSnapshot snapshot,
        int level,
        Vector3Int tileCoord,
        float baseVoxelSize,
        out bool hasAnySolidVoxel,
        int skirtCells = 0)
    {
      BlockChunkData data = LodChunkBuilder.Build(snapshot, level, tileCoord, out hasAnySolidVoxel);

      if (!hasAnySolidVoxel)
      {
        return null;
      }

      BlockGreedyMesher.MaterialLookup lookup = LodChunkBuilder.CreateMaterialLookup(snapshot, level);
      float voxelSize = LodConstants.VoxelSizeForLevel(level, baseVoxelSize);

      MeshData mesh = BlockGreedyMesher.GenerateNeighbourAware(data, lookup, voxelSize);

      // Only skirt tiles that actually show surface; fully enclosed tiles mesh
      // empty and never render, so they need no skirt.
      if (skirtCells > 0 && mesh != null && !mesh.IsEmpty)
      {
        LodSkirtBuilder.AppendSkirts(mesh, data, voxelSize, skirtCells);
      }

      return mesh;
    }

    /// <summary>
    /// Pooled variant: builds the tile and fills <paramref name="meshData"/>
    /// (rented by the caller, e.g. the LOD streamer) instead of allocating.
    /// Returns false and leaves <paramref name="meshData"/> reset/empty when the
    /// tile has no solid cells.
    /// </summary>
    public static bool BuildAndMeshInto(
        WorldGenerationSnapshot snapshot,
        int level,
        Vector3Int tileCoord,
        float baseVoxelSize,
        MeshData meshData,
        int skirtCells = 0)
    {
      BlockChunkData data = LodChunkBuilder.Build(snapshot, level, tileCoord, out bool hasAnySolidVoxel);

      if (meshData == null)
      {
        return false;
      }

      if (!hasAnySolidVoxel)
      {
        meshData.Reset();
        return false;
      }

      BlockGreedyMesher.MaterialLookup lookup = LodChunkBuilder.CreateMaterialLookup(snapshot, level);
      float voxelSize = LodConstants.VoxelSizeForLevel(level, baseVoxelSize);

      BlockGreedyMesher.GenerateNeighbourAware(data, lookup, voxelSize, meshData);

      if (skirtCells > 0 && !meshData.IsEmpty)
      {
        LodSkirtBuilder.AppendSkirts(meshData, data, voxelSize, skirtCells);
      }

      return !meshData.IsEmpty;
    }
  }
}
