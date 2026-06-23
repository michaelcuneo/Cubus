using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace CubusCore.Tests
{
  /// <summary>
  /// Guards the greedy-merge contract of <see cref="BlockGreedyMesher"/>: coplanar
  /// exposed faces of the same material must collapse into a single quad whose UV
  /// spans the full merged extent (one unit per voxel), so the CubusBiomeAtlasURP
  /// shader still tiles the atlas per voxel via frac()/SAMPLE_TEXTURE2D_GRAD. A
  /// regression to per-tile quads would explode the vertex count and break this.
  /// Asset-free: builds chunk data directly and treats out-of-chunk space as air.
  /// </summary>
  [TestFixture]
  public sealed class BlockGreedyMesherTests
  {
    private const int Size = VoxelConstants.ChunkSize;
    private const ushort StoneMaterial = 1;
    private const float VoxelSize = 1.0f;

    // Every quad the mesher emits contributes exactly four vertices, so the quad
    // count is the vertex count divided by four.
    private static ushort AirOutsideChunk(Vector3Int _) => 0;

    [Test]
    public void SingleVoxel_EmitsOneQuadPerExposedFace()
    {
      var chunk = new BlockChunkData(new Vector3Int(0, 0, 0));
      chunk.SetVoxel(1, 1, 1, new Voxel(StoneMaterial));

      MeshData mesh = BlockGreedyMesher.GenerateNeighbourAware(chunk, AirOutsideChunk, VoxelSize);

      // Six exposed faces, each a single quad: 6 quads, 24 verts, 12 triangles.
      Assert.AreEqual(24, mesh.VertexCount, "Isolated voxel should yield six single-quad faces.");
      Assert.AreEqual(12, mesh.TriangleCount, "Each of the six quads is two triangles.");
    }

    [Test]
    public void FullSolidLayer_MergesEachFaceIntoASingleQuad()
    {
      var chunk = new BlockChunkData(new Vector3Int(0, 0, 0));
      for (int x = 0; x < Size; x++)
      {
        for (int z = 0; z < Size; z++)
        {
          chunk.SetVoxel(x, 0, z, new Voxel(StoneMaterial));
        }
      }

      MeshData mesh = BlockGreedyMesher.GenerateNeighbourAware(chunk, AirOutsideChunk, VoxelSize);

      // A 32x32x1 slab exposes exactly six faces (top, bottom, four side strips),
      // each of which must merge to one quad: 6 quads -> 24 verts. Without merging
      // the top and bottom faces alone would be 2 * 32 * 32 * 4 = 8192 verts.
      Assert.AreEqual(24, mesh.VertexCount, "Each coplanar face of the slab must collapse to a single quad.");
      Assert.AreEqual(6, mesh.VertexCount / 4, "Slab should produce six merged quads.");
    }

    [Test]
    public void MergedTopFace_UVSpansFullExtentForPerVoxelTiling()
    {
      var chunk = new BlockChunkData(new Vector3Int(0, 0, 0));
      for (int x = 0; x < Size; x++)
      {
        for (int z = 0; z < Size; z++)
        {
          chunk.SetVoxel(x, 0, z, new Voxel(StoneMaterial));
        }
      }

      MeshData mesh = BlockGreedyMesher.GenerateNeighbourAware(chunk, AirOutsideChunk, VoxelSize);

      // Find the +Y (top) quad and confirm its UVs reach (Size, Size): the shader
      // tiles one atlas cell per integer UV unit, so a merged 32x32 face must carry
      // a UV that runs 0..32 on both axes to reproduce per-voxel texturing.
      float maxU = 0.0f;
      float maxV = 0.0f;
      for (int i = 0; i < mesh.VertexCount; i++)
      {
        if (!Mathf.Approximately(mesh.Normals[i].y, 1.0f)) continue;

        maxU = Mathf.Max(maxU, mesh.UVs[i].x);
        maxV = Mathf.Max(maxV, mesh.UVs[i].y);
      }

      Assert.AreEqual(Size, maxU, 1e-4f, "Merged top face UV.x must span one unit per voxel across the full width.");
      Assert.AreEqual(Size, maxV, 1e-4f, "Merged top face UV.y must span one unit per voxel across the full depth.");
    }

    [Test]
    public void EmptyChunk_ProducesNoGeometry()
    {
      var chunk = new BlockChunkData(new Vector3Int(0, 0, 0));

      MeshData mesh = BlockGreedyMesher.GenerateNeighbourAware(chunk, AirOutsideChunk, VoxelSize);

      Assert.AreEqual(0, mesh.VertexCount, "An all-air chunk must emit no vertices.");
      Assert.IsTrue(mesh.IsEmpty, "An all-air chunk must report as empty.");
    }
  }
}
