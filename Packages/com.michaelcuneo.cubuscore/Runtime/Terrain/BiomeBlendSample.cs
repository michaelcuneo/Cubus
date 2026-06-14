using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  public sealed class BiomeBlendSample
  {
    private readonly List<BiomeBlendContributor> contributors = new();

    public IReadOnlyList<BiomeBlendContributor> Contributors => contributors;
    public int Count => contributors.Count;

    public byte DominantBiomeId { get; private set; }

    public bool IsValid => contributors.Count > 0;

    public void Clear()
    {
      contributors.Clear();
      DominantBiomeId = 0;
    }

    public BiomeBlendContributor GetContributor(int index)
    {
      return contributors[index];
    }

    public void Add(BiomeBlendContributor contributor)
    {
      if (contributor.Weight <= 0.0f)
      {
        return;
      }

      contributors.Add(contributor);

      if (contributors.Count == 1)
      {
        DominantBiomeId = contributor.BiomeId;
      }
      else
      {
        float bestWeight = -1.0f;
        byte bestBiomeId = DominantBiomeId;

        for (int i = 0; i < contributors.Count; i++)
        {
          if (contributors[i].Weight > bestWeight)
          {
            bestWeight = contributors[i].Weight;
            bestBiomeId = contributors[i].BiomeId;
          }
        }

        DominantBiomeId = bestBiomeId;
      }
    }

    public void Normalize()
    {
      float totalWeight = 0.0f;

      for (int i = 0; i < contributors.Count; i++)
      {
        totalWeight += Mathf.Max(0.0f, contributors[i].Weight);
      }

      if (totalWeight <= 0.0001f)
      {
        contributors.Clear();
        DominantBiomeId = 0;
        return;
      }

      float inv = 1.0f / totalWeight;

      float bestWeight = -1.0f;
      byte bestBiomeId = 0;

      for (int i = 0; i < contributors.Count; i++)
      {
        BiomeBlendContributor oldContributor = contributors[i];

        BiomeBlendContributor normalized = new(
            oldContributor.Profile,
            oldContributor.BiomeId,
            Mathf.Max(0.0f, oldContributor.Weight) * inv
        );

        contributors[i] = normalized;

        if (normalized.Weight > bestWeight)
        {
          bestWeight = normalized.Weight;
          bestBiomeId = normalized.BiomeId;
        }
      }

      DominantBiomeId = bestBiomeId;
    }
  }
}