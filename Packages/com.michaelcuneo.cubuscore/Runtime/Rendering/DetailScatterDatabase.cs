using System;
using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering
{
  /// <summary>
  /// Describes the surface foliage scattered on top of a given block material:
  /// which foliage atlas tiles to use, how densely to scatter, and the size /
  /// tint / wind response of each cross-quad cluster.
  /// </summary>
  [Serializable]
  public sealed class DetailScatterProfile
  {
    [Header("Identity")]
    public string DisplayName = "Detail";

    [Tooltip("Surface block material id this foliage scatters on top of.")]
    [Range(1, 65535)]
    public int MaterialId = 1;

    [Header("Atlas Tiles")]
    [Tooltip("Foliage atlas tile indices (row-major, 0 = top-left) randomly chosen per cluster.")]
    public int[] AtlasTiles = new[] { 0 };

    [Header("Density")]
    [Tooltip("Probability (0-1) that an exposed surface voxel grows any foliage at all.")]
    [Range(0f, 1f)]
    public float Coverage = 0.5f;

    [Tooltip("Maximum cross-quad clusters placed on a single surface voxel.")]
    [Range(0, 8)]
    public int MaxClustersPerVoxel = 2;

    [Header("Size (world units)")]
    [Range(0.05f, 4f)] public float MinHeight = 0.5f;
    [Range(0.05f, 4f)] public float MaxHeight = 0.9f;
    [Range(0.05f, 4f)] public float MinWidth = 0.5f;
    [Range(0.05f, 4f)] public float MaxWidth = 0.9f;

    [Header("Appearance")]
    public Color Tint = Color.white;

    [Tooltip("Random per-cluster brightness/hue jitter applied to the tint.")]
    [Range(0f, 1f)]
    public float TintVariation = 0.12f;

    [Header("Wind")]
    [Tooltip("How strongly this foliage sways. Scales the global wind strength.")]
    [Range(0f, 2f)]
    public float SwayStrength = 1f;
  }

  [CreateAssetMenu(
      fileName = "Detail Scatter Database",
      menuName = "CubusCore/Rendering/Detail Scatter Database"
  )]
  public sealed class DetailScatterDatabase : ScriptableObject
  {
    [Header("Foliage Atlas Grid")]
    [Tooltip("Number of tile columns in the foliage atlas texture.")]
    [Range(1, 16)] public int AtlasColumns = 4;

    [Tooltip("Number of tile rows in the foliage atlas texture.")]
    [Range(1, 16)] public int AtlasRows = 4;

    [Tooltip("Maps a surface block material id to the foliage scattered on top of it.")]
    public List<DetailScatterProfile> Profiles = new();

    private Dictionary<ushort, DetailScatterProfile> lookup;

    /// <summary>
    /// Returns the scatter profile for a surface material id, or null if none is configured.
    /// </summary>
    public DetailScatterProfile GetProfile(ushort materialId)
    {
      if (lookup == null)
      {
        RebuildLookup();
      }

      return lookup.TryGetValue(materialId, out DetailScatterProfile profile) ? profile : null;
    }

    public bool HasAnyProfiles => Profiles != null && Profiles.Count > 0;

    private void RebuildLookup()
    {
      lookup = new Dictionary<ushort, DetailScatterProfile>();
      if (Profiles == null)
      {
        return;
      }

      foreach (DetailScatterProfile profile in Profiles)
      {
        if (profile == null)
        {
          continue;
        }

        ushort id = (ushort)Mathf.Clamp(profile.MaterialId, 1, 65535);
        lookup[id] = profile;
      }
    }

    private void OnValidate()
    {
      lookup = null;
    }

    private void OnEnable()
    {
      lookup = null;
    }
  }
}
