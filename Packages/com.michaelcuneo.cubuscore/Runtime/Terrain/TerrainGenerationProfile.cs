using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  [Serializable]
  public sealed class TerrainGenerationProfile
  {
    [Header("Terrain Shape")]
    public bool UseWorldEdgeFalloff = false;

    [Min(1.0f)]
    public float WorldEdgeRadius = 100000.0f;

    public float WorldEdgeTargetHeight = -55.0f;

    public float BaseHeight = 14.0f;

    [Min(0.0f)]
    public float HeightScale = 78.0f;

    [Header("Caves")]
    [Min(0.0f)]
    public float CaveStrength = 0.0f;

    [Min(0.0001f)]
    public float CaveFrequency = 0.035f;

    [Min(0.0f)]
    public float CaveStartDepth = 18.0f;

    [Header("Materials")]
    [Range(1, 65535)]
    public int SurfaceMaterialId = 1;

    [Range(1, 65535)]
    public int SubsurfaceMaterialId = 2;

    [Range(1, 65535)]
    public int StoneMaterialId = 3;
  }
}