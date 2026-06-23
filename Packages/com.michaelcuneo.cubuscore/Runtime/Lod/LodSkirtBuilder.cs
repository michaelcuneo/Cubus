using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod
{
  /// <summary>
  /// Adds downward "skirts" around the four vertical edges of a Distant-Horizon
  /// LOD tile. Where two bands meet at different resolutions, their blocky
  /// surfaces step at different heights and a vertical gap opens between them,
  /// letting the player see through the terrain to the sky/background. A skirt is
  /// a thin curtain dropped straight down from each perimeter column's top solid
  /// voxel; because every tile carries one, adjacent tiles always overlap
  /// vertically by at least the skirt depth and the crack is hidden.
  ///
  /// The curtains are emitted through <see cref="BlockGreedyMesher.AppendGreedyQuad"/>
  /// so they share the terrain's exact UV tiling, winding and packed-material
  /// colour, and they reuse the top voxel's material so they texture like the
  /// surface they hang from.
  /// </summary>
  public static class LodSkirtBuilder
  {
    /// <summary>
    /// Appends perimeter skirts to <paramref name="mesh"/> for the given tile.
    /// Only boundary columns that contain a solid voxel get a skirt, so empty
    /// (above-surface) edges add nothing. No-op when <paramref name="skirtCells"/>
    /// is zero or negative.
    /// </summary>
    public static void AppendSkirts(MeshData mesh, BlockChunkData tile, float voxelSize, int skirtCells)
    {
      if (mesh == null || tile == null || skirtCells <= 0)
      {
        return;
      }

      const int size = VoxelConstants.ChunkSize;
      const int max = size - 1;

      for (int z = 0; z < size; z++)
      {
        // -X edge (plane x = 0, facing -X).
        int topNegX = TopSolidY(tile, 0, z, out ushort matNegX);
        if (topNegX >= 0)
        {
          BlockGreedyMesher.AppendGreedyQuad(
            mesh, axis: 0, startX: 0, startY: topNegX + 1 - skirtCells, startZ: z,
            width: skirtCells, height: 1, positiveFace: false, materialId: matNegX, voxelSize: voxelSize);
        }

        // +X edge (plane x = size, facing +X).
        int topPosX = TopSolidY(tile, max, z, out ushort matPosX);
        if (topPosX >= 0)
        {
          BlockGreedyMesher.AppendGreedyQuad(
            mesh, axis: 0, startX: size, startY: topPosX + 1 - skirtCells, startZ: z,
            width: skirtCells, height: 1, positiveFace: true, materialId: matPosX, voxelSize: voxelSize);
        }
      }

      for (int x = 0; x < size; x++)
      {
        // -Z edge (plane z = 0, facing -Z).
        int topNegZ = TopSolidY(tile, x, 0, out ushort matNegZ);
        if (topNegZ >= 0)
        {
          BlockGreedyMesher.AppendGreedyQuad(
            mesh, axis: 2, startX: x, startY: topNegZ + 1 - skirtCells, startZ: 0,
            width: 1, height: skirtCells, positiveFace: false, materialId: matNegZ, voxelSize: voxelSize);
        }

        // +Z edge (plane z = size, facing +Z).
        int topPosZ = TopSolidY(tile, x, max, out ushort matPosZ);
        if (topPosZ >= 0)
        {
          BlockGreedyMesher.AppendGreedyQuad(
            mesh, axis: 2, startX: x, startY: topPosZ + 1 - skirtCells, startZ: size,
            width: 1, height: skirtCells, positiveFace: true, materialId: matPosZ, voxelSize: voxelSize);
        }
      }
    }

    // Highest solid voxel in the (x, z) column, or -1 if the column is empty.
    private static int TopSolidY(BlockChunkData tile, int x, int z, out ushort material)
    {
      for (int y = VoxelConstants.ChunkSize - 1; y >= 0; y--)
      {
        if (tile.IsSolid(x, y, z))
        {
          material = tile.GetVoxel(x, y, z).MaterialId;
          return y;
        }
      }

      material = 0;
      return -1;
    }
  }
}
