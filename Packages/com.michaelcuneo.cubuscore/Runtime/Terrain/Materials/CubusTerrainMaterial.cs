using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain.Materials
{
  [Serializable]
  public sealed class CubusTerrainMaterial
  {
    [Header("Identity")]
    [Range(1, 65535)]
    public int MaterialId = 1;

    public string DisplayName = "Terrain Material";

    [Header("Textures")]
    public Texture2D Albedo;

    [Tooltip("Normal map texture. Import this as Normal Map in Unity.")]
    public Texture2D Normal;

    [Tooltip("Packed mask texture. Recommended: R = AO, G = Roughness, B = Metallic, A = Height.")]
    public Texture2D Mask;

    [Header("Surface")]
    [Min(0.001f)]
    public float Tiling = 1.0f;

    [Range(0.0f, 4.0f)]
    public float NormalStrength = 1.0f;

    [Range(0.0f, 1.0f)]
    public float RoughnessFallback = 0.8f;

    [Range(0.0f, 1.0f)]
    public float MetallicFallback = 0.0f;

    [Range(0.0f, 1.0f)]
    public float HeightStrength = 0.0f;
  }
}