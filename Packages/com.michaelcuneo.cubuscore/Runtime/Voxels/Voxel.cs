using System;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels
{
  [Serializable]
  public struct Voxel
  {
    public ushort MaterialId;

    public bool IsAir => MaterialId == 0;
    public bool IsSolid => MaterialId != 0;

    public Voxel(ushort materialId)
    {
      MaterialId = materialId;
    }

    public static Voxel Air => new(0);
  }
}