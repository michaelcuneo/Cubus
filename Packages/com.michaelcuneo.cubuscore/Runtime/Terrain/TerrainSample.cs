using System;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  [Serializable]
  public struct TerrainSample
  {
    public float Density;
    public int SolidMaterialId;
    public int LiquidMaterialId;
    public byte BiomeId;
    public float SurfaceHeight;
    public float CaveAmount;
    public bool IsLiquid;

    /// <summary>
    /// Weighted terrain/geology materials for density/splat terrain.
    /// If empty, consumers should fall back to SolidMaterialId.
    /// </summary>
    public DensityMaterialSet Materials;
  }
}