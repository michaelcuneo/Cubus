using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Water
{
  /// <summary>
  /// Keeps voxel water disabled while terrain and player spawn coverage are being
  /// prepared. Water begins streaming only after initial terrain readiness fires.
  /// </summary>
  [DisallowMultipleComponent]
  [DefaultExecutionOrder(-15)]
  internal sealed class VoxelWaterStartupGate : MonoBehaviour
  {
    private CubusWorld world;
    private VoxelWaterSystem water;

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
      water = GetComponent<VoxelWaterSystem>();

      if (water != null && (world == null || !world.IsInitialTerrainReady))
      {
        water.enabled = false;
      }
    }

    private void Update()
    {
      if (water == null)
      {
        enabled = false;
        return;
      }

      if (world == null || !world.IsInitialTerrainReady)
      {
        return;
      }

      water.enabled = true;
      Destroy(this);
    }
  }
}
