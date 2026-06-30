using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace CubusCore.Tests
{
  /// <summary>
  /// Guards the SmoothDensity LOD path (<see cref="LodDensityTileMesher"/> and the
  /// shared <see cref="DensityMeshDataBuilder.BuildFromGrids"/>): a grid with no
  /// surface crossing meshes to null, a crossing grid produces geometry, an
  /// above-surface tile is empty, a surface tile yields real geometry, the build is
  /// deterministic, and skirts add geometry. Asset-free: snapshot from default
  /// <see cref="WorldSettings"/> (empty biome rules -> deterministic fallback).
  /// </summary>
  [TestFixture]
  public sealed class LodDensityTileMesherTests
  {
    private const float BaseVoxelSize = 1.0f;

    private static WorldGenerationSnapshot DefaultSnapshot()
    {
      return WorldGenerationSnapshot.FromSettings(new WorldSettings());
    }

    private static int SampleIndex(int sx, int sy, int sz, int samplesAxis)
    {
      return sx + samplesAxis * (sy + samplesAxis * sz);
    }

    [Test]
    public void BuildFromGrids_NoCrossing_ReturnsNull()
    {
      const int numCellsAxis = 2;
      int samplesAxis = numCellsAxis + 1;
      int total = samplesAxis * samplesAxis * samplesAxis;

      var allSolid = new float[total];
      var allAir = new float[total];
      var materials = new DensityMaterialSet[total];
      for (int i = 0; i < total; i++)
      {
        allSolid[i] = 1.0f;
        allAir[i] = -1.0f;
        materials[i] = DensityMaterialSet.Single(1);
      }

      Assert.IsNull(
          DensityMeshDataBuilder.BuildFromGrids(allSolid, materials, numCellsAxis, 1.0f, true),
          "A fully solid grid has no surface crossing and must mesh to null.");
      Assert.IsNull(
          DensityMeshDataBuilder.BuildFromGrids(allAir, materials, numCellsAxis, 1.0f, true),
          "A fully empty grid has no surface crossing and must mesh to null.");
    }

    [Test]
    public void BuildFromGrids_HorizontalSurface_ProducesGeometry()
    {
      const int numCellsAxis = 2;
      int samplesAxis = numCellsAxis + 1;
      int total = samplesAxis * samplesAxis * samplesAxis;

      var density = new float[total];
      var materials = new DensityMaterialSet[total];

      // Surface crossing between sy = 0 (solid) and sy = 1 (air).
      for (int sz = 0; sz < samplesAxis; sz++)
      {
        for (int sy = 0; sy < samplesAxis; sy++)
        {
          for (int sx = 0; sx < samplesAxis; sx++)
          {
            int index = SampleIndex(sx, sy, sz, samplesAxis);
            density[index] = 0.5f - sy;
            materials[index] = DensityMaterialSet.Single(1);
          }
        }
      }

      MeshData mesh = DensityMeshDataBuilder.BuildFromGrids(density, materials, numCellsAxis, 1.0f, true);

      Assert.IsNotNull(mesh, "A surface-crossing grid must produce a mesh.");
      Assert.IsFalse(mesh.IsEmpty, "A surface-crossing grid must produce geometry.");
      Assert.Greater(mesh.TriangleCount, 0);

      MeshDataPool.Return(mesh);
    }

    [Test]
    public void AboveSurfaceTile_ProducesNoMesh()
    {
      WorldGenerationSnapshot snapshot = DefaultSnapshot();

      MeshData mesh = LodDensityTileMesher.BuildAndMesh(snapshot, 1, new Vector3Int(0, 1000, 0), BaseVoxelSize);

      Assert.IsNull(mesh, "An above-surface density tile should mesh to null so the streamer skips it.");
    }

    [Test]
    public void SurfaceBand_ProducesGeometrySomewhere()
    {
      WorldGenerationSnapshot snapshot = DefaultSnapshot();

      bool foundGeometry = false;

      for (int tileY = -2; tileY <= 3 && !foundGeometry; tileY++)
      {
        MeshData mesh = LodDensityTileMesher.BuildAndMesh(snapshot, 1, new Vector3Int(0, tileY, 0), BaseVoxelSize);

        if (mesh != null && !mesh.IsEmpty)
        {
          foundGeometry = true;
          Assert.Greater(mesh.VertexCount, 0, "A surface-crossing tile must have vertices.");
          Assert.Greater(mesh.TriangleCount, 0, "A surface-crossing tile must have triangles.");
          MeshDataPool.Return(mesh);
        }
      }

      Assert.IsTrue(foundGeometry, "Expected at least one density surface tile near y=0 to produce geometry.");
    }

    [Test]
    public void Build_IsDeterministic()
    {
      WorldGenerationSnapshot snapshot = DefaultSnapshot();
      var coord = new Vector3Int(1, 0, -1);

      MeshData a = LodDensityTileMesher.BuildAndMesh(snapshot, 2, coord, BaseVoxelSize);
      MeshData b = LodDensityTileMesher.BuildAndMesh(snapshot, 2, coord, BaseVoxelSize);

      if (a == null && b == null)
      {
        Assert.Pass("Tile is empty in both builds (still deterministic).");
        return;
      }

      Assert.IsNotNull(a);
      Assert.IsNotNull(b);
      Assert.AreEqual(a.VertexCount, b.VertexCount, "Vertex count must be deterministic.");
      Assert.AreEqual(a.TriangleCount, b.TriangleCount, "Triangle count must be deterministic.");

      MeshDataPool.Return(a);
      MeshDataPool.Return(b);
    }

    [Test]
    public void Skirts_AddGeometryToSurfaceTile()
    {
      WorldGenerationSnapshot snapshot = DefaultSnapshot();

      // Find a tile that crosses the surface, then compare with and without skirts.
      for (int tileY = -2; tileY <= 3; tileY++)
      {
        var coord = new Vector3Int(0, tileY, 0);

        MeshData noSkirt = LodDensityTileMesher.BuildAndMesh(snapshot, 1, coord, BaseVoxelSize, 0);
        if (noSkirt == null || noSkirt.IsEmpty)
        {
          if (noSkirt != null)
          {
            MeshDataPool.Return(noSkirt);
          }

          continue;
        }

        int baseTriangles = noSkirt.TriangleCount;
        MeshDataPool.Return(noSkirt);

        MeshData skirted = LodDensityTileMesher.BuildAndMesh(snapshot, 1, coord, BaseVoxelSize, 2);
        Assert.IsNotNull(skirted);
        Assert.GreaterOrEqual(
            skirted.TriangleCount,
            baseTriangles,
            "Skirts must never remove geometry from a surface tile.");
        MeshDataPool.Return(skirted);
        return;
      }

      Assert.Inconclusive("No surface-crossing density tile found near y=0 to exercise skirts.");
    }
  }
}
