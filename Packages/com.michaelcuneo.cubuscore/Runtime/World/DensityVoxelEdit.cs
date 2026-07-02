using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  /// <summary>
  /// A single resolved smooth-density voxel change (absolute density + material) produced by a
  /// sculpt. Used to replicate density edits per voxel (idempotent, last-write-wins) instead of
  /// syncing whole chunks.
  /// </summary>
  public readonly struct DensityVoxelEdit
  {
    public readonly Vector3Int Voxel;
    public readonly float Density;
    public readonly ushort Material;

    public DensityVoxelEdit(Vector3Int voxel, float density, ushort material)
    {
      Voxel = voxel;
      Density = density;
      Material = material;
    }
  }
}
