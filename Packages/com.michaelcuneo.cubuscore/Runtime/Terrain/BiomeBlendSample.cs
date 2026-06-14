using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  public struct BiomeBlendSample
  {
    public BiomeBlendContributor Contributor0;
    public BiomeBlendContributor Contributor1;
    public BiomeBlendContributor Contributor2;
    public BiomeBlendContributor Contributor3;

    public int Count;
    public byte DominantBiomeId;

    public bool IsValid => Count > 0;

    public BiomeBlendContributor GetContributor(int index)
    {
      return index switch
      {
        0 => Contributor0,
        1 => Contributor1,
        2 => Contributor2,
        3 => Contributor3,
        _ => Contributor0
      };
    }

    public void Normalize()
    {
      float total = 0.0f;

      if (Count > 0) total += Contributor0.Weight;
      if (Count > 1) total += Contributor1.Weight;
      if (Count > 2) total += Contributor2.Weight;
      if (Count > 3) total += Contributor3.Weight;

      if (total <= 0.0001f)
      {
        return;
      }

      float inv = 1.0f / total;

      if (Count > 0)
      {
        Contributor0 = new BiomeBlendContributor(
            Contributor0.Profile,
            Contributor0.BiomeId,
            Mathf.Clamp01(Contributor0.Weight * inv)
        );

        DominantBiomeId = Contributor0.BiomeId;
      }

      if (Count > 1)
      {
        Contributor1 = new BiomeBlendContributor(
            Contributor1.Profile,
            Contributor1.BiomeId,
            Mathf.Clamp01(Contributor1.Weight * inv)
        );
      }

      if (Count > 2)
      {
        Contributor2 = new BiomeBlendContributor(
            Contributor2.Profile,
            Contributor2.BiomeId,
            Mathf.Clamp01(Contributor2.Weight * inv)
        );
      }

      if (Count > 3)
      {
        Contributor3 = new BiomeBlendContributor(
            Contributor3.Profile,
            Contributor3.BiomeId,
            Mathf.Clamp01(Contributor3.Weight * inv)
        );
      }
    }
  }
}