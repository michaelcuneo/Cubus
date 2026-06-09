using System;

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
  }
}