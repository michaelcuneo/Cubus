using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  [Serializable]
  public readonly struct TerrainGenerationProfileSnapshot
  {
    public readonly bool UseWorldEdgeFalloff;
    public readonly float WorldEdgeRadius;
    public readonly float WorldEdgeTargetHeight;
    public readonly float BaseHeight;
    public readonly float HeightScale;

    public readonly float CaveStrength;
    public readonly float CaveFrequency;
    public readonly float CaveStartDepth;

    public readonly int SurfaceMaterialId;
    public readonly int SubsurfaceMaterialId;
    public readonly int StoneMaterialId;

    public TerrainGenerationProfileSnapshot(TerrainGenerationProfile profile)
    {
      if (profile == null)
      {
        profile = new TerrainGenerationProfile();
      }

      UseWorldEdgeFalloff = profile.UseWorldEdgeFalloff;
      WorldEdgeRadius = profile.WorldEdgeRadius;
      WorldEdgeTargetHeight = profile.WorldEdgeTargetHeight;
      BaseHeight = profile.BaseHeight;
      HeightScale = profile.HeightScale;

      CaveStrength = profile.CaveStrength;
      CaveFrequency = profile.CaveFrequency;
      CaveStartDepth = profile.CaveStartDepth;

      SurfaceMaterialId = profile.SurfaceMaterialId;
      SubsurfaceMaterialId = profile.SubsurfaceMaterialId;
      StoneMaterialId = profile.StoneMaterialId;
    }
  }
}