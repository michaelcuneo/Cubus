using System;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels
{
  [Serializable]
  public struct DensityVoxel
  {
    public float Density;
    public ushort MaterialId;

    public bool IsSolid => Density > 0.0f;

    public DensityVoxel(float density, ushort materialId)
    {
      Density = density;
      MaterialId = density > 0.0f ? materialId : (ushort)0;
    }

    public static DensityVoxel Empty => new(-1.0f, 0);
  }
}