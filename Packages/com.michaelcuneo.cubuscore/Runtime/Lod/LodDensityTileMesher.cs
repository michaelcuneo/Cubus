using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod
{
  /// <summary>
  /// Meshes a Distant-Horizon LOD tile for SmoothDensity terrain. It samples the
  /// world snapshot's density field at the level's coarse stride (one coarse cell
  /// = 2^level real voxels) and feeds the grids through the shared marching-cubes
  /// builder (<see cref="DensityMeshDataBuilder.BuildFromGrids"/>). Sampling and
  /// vertex positioning mirror <c>DensityChunkBuilder</c>/<c>DensityMeshDataBuilder</c>
  /// exactly, so a far tile is just a coarser version of the same smooth surface a
  /// full chunk would produce - reusing the identical biome atlas material,
  /// interpolation and winding, and lining up seamlessly in world space.
  ///
  /// A tile is a pure function of the snapshot, so (like the block LOD path) it is
  /// built off the main thread and never needs re-meshing when chunks stream in.
  /// </summary>
  public static class LodDensityTileMesher
  {
    /// <summary>
    /// Builds and meshes a SmoothDensity tile. Returns <c>null</c> when the tile
    /// contains no surface crossing (caller skips it). When
    /// <paramref name="skirtCells"/> &gt; 0 and the tile has visible surface,
    /// downward perimeter curtains are appended to hide cracks at band boundaries.
    /// </summary>
    public static MeshData BuildAndMesh(
        WorldGenerationSnapshot snapshot,
        int level,
        Vector3Int tileCoord,
        float baseVoxelSize,
        int skirtCells = 0)
    {
      int stride = LodConstants.StrideForLevel(level);
      int numCellsAxis = VoxelConstants.ChunkSize;
      int numSamplesAxis = numCellsAxis + 1;
      int totalSamples = numSamplesAxis * numSamplesAxis * numSamplesAxis;

      // One coarse cell spans `stride` real voxels, so the tile's voxel size is
      // 2^level * baseVoxelSize - exactly how the LOD renderer positions it.
      float cellWorldSize = LodConstants.VoxelSizeForLevel(level, baseVoxelSize);
      float densityScale = Mathf.Max(0.001f, snapshot.DensitySampleScale);

      // World-voxel origin of the tile (in real voxels), matching how a full chunk
      // at the equivalent coordinate would be placed.
      Vector3Int origin = tileCoord * (numCellsAxis * stride);

      float[] densityGrid = new float[totalSamples];
      DensityMaterialSet[] materialGrid = new DensityMaterialSet[totalSamples];

      for (int sz = 0; sz < numSamplesAxis; sz++)
      {
        int worldZ = origin.z + sz * stride;

        for (int sx = 0; sx < numSamplesAxis; sx++)
        {
          int worldX = origin.x + sx * stride;

          // Biome blend depends only on (x, z); resolve once per column to match
          // DensityChunkBuilder and keep sampling cheap.
          BiomeBlendSample blend = snapshot.ResolveBiomeBlendAtWorldXZ(worldX, worldZ);

          for (int sy = 0; sy < numSamplesAxis; sy++)
          {
            Vector3Int worldVoxel = new(worldX, origin.y + sy * stride, worldZ);
            TerrainSample sample = BiomeTerrainSampler.Sample(snapshot, blend, worldVoxel, densityScale);

            DensityMaterialSet materials = sample.Density > 0.0f
              ? sample.Materials
              : DensityMaterialSet.Empty;

            if (sample.Density > 0.0f && materials.DominantMaterialId == 0)
            {
              ushort fallbackMaterialId = (ushort)Mathf.Clamp(sample.SolidMaterialId, 1, 65535);
              materials = DensityMaterialSet.Single(fallbackMaterialId);
            }

            int index = sx + numSamplesAxis * (sy + numSamplesAxis * sz);
            densityGrid[index] = sample.Density;
            materialGrid[index] = materials;
          }
        }
      }

      MeshData mesh = DensityMeshDataBuilder.BuildFromGrids(
          densityGrid,
          materialGrid,
          numCellsAxis,
          cellWorldSize,
          flipWinding: true);

      if (skirtCells > 0 && mesh != null && !mesh.IsEmpty)
      {
        LodDensitySkirtBuilder.AppendSkirts(
            mesh,
            numCellsAxis * cellWorldSize,
            skirtCells * cellWorldSize);
      }

      return mesh;
    }
  }
}
