using System;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels
{
  [Serializable]
  public struct DensityVoxel
  {
    public float Density;

    /// <summary>
    /// Dominant/fallback material. Kept for old systems, debug tools, saves,
    /// simple edits, and any path that has not migrated to weighted materials yet.
    /// </summary>
    public ushort MaterialId;

    /// <summary>
    /// Weighted material set for voxel-steered splat terrain.
    /// </summary>
    public DensityMaterialSet Materials;

    public bool IsSolid => Density > 0.0f;

    public DensityVoxel(float density, ushort materialId)
    {
      Density = density;
      MaterialId = density > 0.0f ? materialId : (ushort)0;
      Materials = density > 0.0f
        ? DensityMaterialSet.Single(materialId)
        : DensityMaterialSet.Empty;
    }

    public DensityVoxel(float density, DensityMaterialSet materials)
    {
      Density = density;
      Materials = density > 0.0f ? materials.Normalized() : DensityMaterialSet.Empty;
      MaterialId = density > 0.0f ? Materials.DominantMaterialId : (ushort)0;
    }

    public static DensityVoxel Empty => new(-1.0f, 0);
  }

  [Serializable]
  public struct DensityMaterialSet
  {
    public ushort Material0;
    public ushort Material1;
    public ushort Material2;
    public ushort Material3;

    public byte Weight0;
    public byte Weight1;
    public byte Weight2;
    public byte Weight3;

    public static DensityMaterialSet Empty => default;

    public static DensityMaterialSet Single(ushort materialId)
    {
      if (materialId == 0)
      {
        return Empty;
      }

      return new DensityMaterialSet
      {
        Material0 = materialId,
        Material1 = 0,
        Material2 = 0,
        Material3 = 0,
        Weight0 = 255,
        Weight1 = 0,
        Weight2 = 0,
        Weight3 = 0
      };
    }

    public ushort DominantMaterialId
    {
      get
      {
        ushort result = Material0;
        byte best = Weight0;

        if (Weight1 > best)
        {
          best = Weight1;
          result = Material1;
        }

        if (Weight2 > best)
        {
          best = Weight2;
          result = Material2;
        }

        if (Weight3 > best)
        {
          result = Material3;
        }

        return result;
      }
    }

    public DensityMaterialSet Normalized()
    {
      int total = Weight0 + Weight1 + Weight2 + Weight3;

      if (total <= 0)
      {
        return Material0 != 0 ? Single(Material0) : Empty;
      }

      DensityMaterialSet result = this;

      result.Weight0 = (byte)Math.Clamp((Weight0 * 255 + total / 2) / total, 0, 255);
      result.Weight1 = (byte)Math.Clamp((Weight1 * 255 + total / 2) / total, 0, 255);
      result.Weight2 = (byte)Math.Clamp((Weight2 * 255 + total / 2) / total, 0, 255);

      int used = result.Weight0 + result.Weight1 + result.Weight2;
      result.Weight3 = (byte)Math.Clamp(255 - used, 0, 255);

      return result;
    }
  }
}