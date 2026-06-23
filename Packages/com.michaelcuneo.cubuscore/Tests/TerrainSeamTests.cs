using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using NUnit.Framework;
using UnityEngine;

namespace CubusCore.Tests
{
  /// <summary>
  /// Roadmap item 8 - Bounds / seam continuity. Verifies that adjacent chunks tile
  /// the world voxel grid with no gaps or overlap, and that the generated density
  /// field is continuous across chunk boundaries (a coordinate-offset or seam bug
  /// would show up as a discontinuity far larger than the field's natural gradient).
  /// Asset-free: uses the deterministic fallback profile via CubusTestFactory.
  /// </summary>
  [TestFixture]
  public sealed class TerrainSeamTests
  {
    private const int Size = VoxelConstants.ChunkSize;

    // A boundary step is just another adjacent-voxel step in the continuous field,
    // so it must not exceed the field's natural in-chunk gradient by much.
    private const float ContinuityFactor = 1.5f;
    private const float ContinuityEpsilon = 1e-3f;

    private enum Axis
    {
      X,
      Z
    }

    [Test]
    public void AdjacentChunks_WorldVoxelRanges_AreContiguous_NoGapsOrOverlap()
    {
      var coordA = new Vector3Int(0, 0, 0);
      var coordB = new Vector3Int(1, 0, 0);

      for (int y = 0; y < Size; y++)
      {
        for (int z = 0; z < Size; z++)
        {
          Vector3Int edgeA = VoxelMath.LocalToWorldVoxel(coordA, Size - 1, y, z);
          Vector3Int edgeB = VoxelMath.LocalToWorldVoxel(coordB, 0, y, z);

          Assert.AreEqual(edgeA.x + 1, edgeB.x, $"Seam gap/overlap on X at (y={y}, z={z}).");
          Assert.AreEqual(edgeA.y, edgeB.y, $"Y drift across X seam at (y={y}, z={z}).");
          Assert.AreEqual(edgeA.z, edgeB.z, $"Z drift across X seam at (y={y}, z={z}).");
        }
      }
    }

    [Test]
    public void DensityField_IsContinuous_AcrossXBoundary()
    {
      AssertContinuousSeam(new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), Axis.X);
    }

    [Test]
    public void DensityField_IsContinuous_AcrossZBoundary()
    {
      AssertContinuousSeam(new Vector3Int(0, 0, 0), new Vector3Int(0, 0, 1), Axis.Z);
    }

    private static void AssertContinuousSeam(Vector3Int coordA, Vector3Int coordB, Axis axis)
    {
      WorldSettings settings = CubusTestFactory.BuildSettings(TerrainSystem.SmoothDensity, worldSeed: 555);
      var generator = new WorldGenerator(settings);

      var chunkA = new DensityChunkData(coordA);
      generator.FillDensityChunkFromTerrainSampler(chunkA);

      var chunkB = new DensityChunkData(coordB);
      generator.FillDensityChunkFromTerrainSampler(chunkB);

      // The natural adjacent-voxel gradient of the field around the seam.
      float maxInChunkGap = Mathf.Max(
        MaxAdjacentGap(chunkA, axis),
        MaxAdjacentGap(chunkB, axis));

      float maxBoundaryGap = 0.0f;
      for (int a = 0; a < Size; a++)
      {
        for (int b = 0; b < Size; b++)
        {
          float edgeA = SampleSeamPlane(chunkA, axis, Size - 1, a, b);
          float edgeB = SampleSeamPlane(chunkB, axis, 0, a, b);
          maxBoundaryGap = Mathf.Max(maxBoundaryGap, Mathf.Abs(edgeA - edgeB));
        }
      }

      Assert.LessOrEqual(
        maxBoundaryGap,
        (maxInChunkGap * ContinuityFactor) + ContinuityEpsilon,
        $"Density discontinuity across {axis} seam: boundaryGap={maxBoundaryGap:0.0000}, " +
        $"maxInChunkGap={maxInChunkGap:0.0000} (possible chunk-offset / seam bug).");
    }

    // Reads density at position 'main' along the seam axis; (a, b) index the two
    // perpendicular axes.
    private static float SampleSeamPlane(DensityChunkData chunk, Axis axis, int main, int a, int b)
    {
      return axis == Axis.X
        ? chunk.GetVoxel(main, a, b).Density
        : chunk.GetVoxel(a, b, main).Density;
    }

    private static float MaxAdjacentGap(DensityChunkData chunk, Axis axis)
    {
      float max = 0.0f;
      for (int main = 0; main < Size - 1; main++)
      {
        for (int a = 0; a < Size; a++)
        {
          for (int b = 0; b < Size; b++)
          {
            float d0 = SampleSeamPlane(chunk, axis, main, a, b);
            float d1 = SampleSeamPlane(chunk, axis, main + 1, a, b);
            max = Mathf.Max(max, Mathf.Abs(d0 - d1));
          }
        }
      }

      return max;
    }
  }
}
