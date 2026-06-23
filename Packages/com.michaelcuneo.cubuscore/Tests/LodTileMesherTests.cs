using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using NUnit.Framework;
using UnityEngine;

namespace CubusCore.Tests
{
  /// <summary>
  /// Guards the LOD meshing path (<see cref="LodTileMesher"/>): empty (above-surface)
  /// tiles produce no mesh, a fully-enclosed deep tile culls every face (single-sided
  /// mesher + solid coarse neighbours), and a tile crossing the surface produces real
  /// geometry. Asset-free: snapshot from default <see cref="WorldSettings"/>.
  /// </summary>
  [TestFixture]
  public sealed class LodTileMesherTests
  {
    private const float BaseVoxelSize = 1.0f;

    private static WorldGenerationSnapshot DefaultSnapshot()
    {
      return WorldGenerationSnapshot.FromSettings(new WorldSettings());
    }

    [Test]
    public void AboveSurfaceTile_ProducesNoMesh()
    {
      WorldGenerationSnapshot snapshot = DefaultSnapshot();

      MeshData mesh = LodTileMesher.BuildAndMesh(snapshot, 1, new Vector3Int(0, 1000, 0), BaseVoxelSize, out bool hasSolid);

      Assert.IsFalse(hasSolid, "An above-surface tile has no solid cells.");
      Assert.IsNull(mesh, "An empty tile should mesh to null so the streamer skips it.");

      var target = new MeshData();
      bool produced = LodTileMesher.BuildAndMeshInto(snapshot, 1, new Vector3Int(0, 1000, 0), BaseVoxelSize, target);

      Assert.IsFalse(produced, "BuildAndMeshInto must report no geometry for an empty tile.");
      Assert.IsTrue(target.IsEmpty, "Provided mesh must be left empty for an empty tile.");
    }

    [Test]
    public void FullyEnclosedDeepTile_MeshesEmpty()
    {
      // A deep tile is solid everywhere and its coarse neighbours (via the lookup)
      // are solid too, so the single-sided mesher culls every face.
      WorldGenerationSnapshot snapshot = DefaultSnapshot();

      MeshData mesh = LodTileMesher.BuildAndMesh(snapshot, 1, new Vector3Int(0, -1000, 0), BaseVoxelSize, out bool hasSolid);

      Assert.IsTrue(hasSolid, "A deep tile is solid.");
      Assert.IsNotNull(mesh, "A solid tile still returns a (possibly empty) mesh.");
      Assert.IsTrue(mesh.IsEmpty, "A fully enclosed tile must emit no geometry.");
    }

    [Test]
    public void SurfaceBand_ProducesGeometrySomewhere()
    {
      // The surface must cross one of the tiles stacked around y = 0, and that tile
      // must emit a non-empty mesh.
      WorldGenerationSnapshot snapshot = DefaultSnapshot();

      bool foundGeometry = false;

      for (int tileY = -2; tileY <= 3 && !foundGeometry; tileY++)
      {
        MeshData mesh = LodTileMesher.BuildAndMesh(snapshot, 1, new Vector3Int(0, tileY, 0), BaseVoxelSize, out _);

        if (mesh != null && !mesh.IsEmpty)
        {
          foundGeometry = true;
          Assert.Greater(mesh.VertexCount, 0, "A surface-crossing tile must have vertices.");
          Assert.Greater(mesh.TriangleCount, 0, "A surface-crossing tile must have triangles.");
        }
      }

      Assert.IsTrue(foundGeometry, "Expected at least one surface tile near y=0 to produce geometry.");
    }
  }
}
