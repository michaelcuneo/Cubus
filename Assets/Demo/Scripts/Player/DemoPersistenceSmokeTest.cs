using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence;
using UnityEngine;

namespace Assets.Demo.Scripts.Player
{
  [RequireComponent(typeof(WorldPersistence))]
  public sealed class DemoPersistenceSmokeTest : MonoBehaviour
  {
    private WorldPersistence persistence;

    private void Awake()
    {
      persistence = GetComponent<WorldPersistence>();
    }

    [ContextMenu("Run World Persistence Smoke Test")]
    public void RunWorldRoundTrip()
    {
      bool saved = persistence != null && persistence.SaveDefaultWorld();
      bool loaded = saved && persistence.LoadDefaultWorld();

      Debug.Log($"World persistence smoke test result. Saved={saved}, Loaded={loaded}");
    }

    [ContextMenu("Run Legacy Block Persistence Smoke Test")]
    public void RunBlockRoundTrip()
    {
      bool saved = persistence != null && persistence.SaveDefaultBlockWorld();
      bool loaded = saved && persistence.LoadDefaultBlockWorld();

      Debug.Log($"Legacy block persistence smoke test result. Saved={saved}, Loaded={loaded}");
    }
  }
}