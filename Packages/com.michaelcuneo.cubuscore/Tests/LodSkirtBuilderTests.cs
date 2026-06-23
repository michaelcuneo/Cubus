using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace CubusCore.Tests
{
  /// <summary>
  /// Verifies the Distant-Horizon skirt builder: it adds downward perimeter
  /// curtains only for boundary columns that contain solid voxels, hangs them
  /// from the column's top, and is a no-op when disabled.
  /// </summary>
  [TestFixture]
  public sealed class LodSkirtBuilderTests
  {
    private const int Size = VoxelConstants.ChunkSize;
    private const float VoxelSize = 1.0f;

    private static BlockChunkData SolidFloorTile(int floorTopY)
    {
      var tile = new BlockChunkData(Vector3Int.zero);
      for (int z = 0; z < Size; z++)
      {
        for (int x = 0; x < Size; x++)
        {
          for (int y = 0; y <= floorTopY; y++)
          {
            tile.SetVoxel(x, y, z, new Voxel(1));
          }
        }
      }

      return tile;
    }

    [Test]
    public void ZeroSkirtCells_AddsNothing()
    {
      var mesh = new MeshData();
      BlockChunkData tile = SolidFloorTile(4);

      LodSkirtBuilder.AppendSkirts(mesh, tile, VoxelSize, 0);

      Assert.AreEqual(0, mesh.VertexCount, "A zero skirt depth must add no geometry.");
    }

    [Test]
    public void EmptyTile_AddsNothing()
    {
      var mesh = new MeshData();
      var tile = new BlockChunkData(Vector3Int.zero);

      LodSkirtBuilder.AppendSkirts(mesh, tile, VoxelSize, 2);

      Assert.AreEqual(0, mesh.VertexCount, "An all-air tile has no boundary columns to skirt.");
    }

    [Test]
    public void SolidFloor_AddsSkirtOnEveryPerimeterColumn()
    {
      var mesh = new MeshData();
      const int floorTopY = 4;
      const int skirtCells = 2;
      BlockChunkData tile = SolidFloorTile(floorTopY);

      LodSkirtBuilder.AppendSkirts(mesh, tile, VoxelSize, skirtCells);

      // 4 edges * Size columns, one quad (4 verts / 6 indices) each.
      int expectedQuads = 4 * Size;
      Assert.AreEqual(expectedQuads * 4, mesh.VertexCount, "Expected one skirt quad per perimeter column.");
      Assert.AreEqual(expectedQuads * 6, mesh.Triangles.Count, "Each skirt quad must add two triangles.");
    }

    [Test]
    public void Skirt_HangsFromColumnTopDownByDepth()
    {
      var mesh = new MeshData();
      const int floorTopY = 4;
      const int skirtCells = 2;
      BlockChunkData tile = SolidFloorTile(floorTopY);

      LodSkirtBuilder.AppendSkirts(mesh, tile, VoxelSize, skirtCells);

      // Top of the floor voxel is at plane (floorTopY + 1); the skirt spans
      // [top - skirtCells, top] in world Y.
      float expectedTop = (floorTopY + 1) * VoxelSize;
      float expectedBottom = expectedTop - skirtCells * VoxelSize;

      float meshTop = float.MinValue;
      float meshBottom = float.MaxValue;
      foreach (Vector3 vertex in mesh.Vertices)
      {
        meshTop = Mathf.Max(meshTop, vertex.y);
        meshBottom = Mathf.Min(meshBottom, vertex.y);
      }

      Assert.AreEqual(expectedTop, meshTop, 1e-4f, "Skirt should reach the top of the column.");
      Assert.AreEqual(expectedBottom, meshBottom, 1e-4f, "Skirt should drop down by the configured depth.");
    }

    [Test]
    public void Skirt_NormalsPointOutward()
    {
      var mesh = new MeshData();
      BlockChunkData tile = SolidFloorTile(4);

      LodSkirtBuilder.AppendSkirts(mesh, tile, VoxelSize, 2);

      bool hasNegX = false, hasPosX = false, hasNegZ = false, hasPosZ = false;
      foreach (Vector3 normal in mesh.Normals)
      {
        if (Mathf.Approximately(normal.x, -1.0f)) hasNegX = true;
        if (Mathf.Approximately(normal.x, 1.0f)) hasPosX = true;
        if (Mathf.Approximately(normal.z, -1.0f)) hasNegZ = true;
        if (Mathf.Approximately(normal.z, 1.0f)) hasPosZ = true;
      }

      Assert.IsTrue(hasNegX && hasPosX && hasNegZ && hasPosZ,
        "Skirts must face outward on all four horizontal edges.");
    }
  }
}
