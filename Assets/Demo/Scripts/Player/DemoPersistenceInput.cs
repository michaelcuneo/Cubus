using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Assets.Demo.Scripts.Player
{
  [RequireComponent(typeof(WorldPersistence))]
  public sealed class DemoPersistenceInput : MonoBehaviour
  {
    private WorldPersistence persistence;

    private void Awake()
    {
      persistence = GetComponent<WorldPersistence>();
    }

    private void Update()
    {
      Keyboard keyboard = Keyboard.current;

      if (keyboard == null)
      {
        return;
      }

      if (keyboard.f5Key.wasPressedThisFrame)
      {
        persistence.SaveDefaultWorld();
      }

      if (keyboard.f9Key.wasPressedThisFrame)
      {
        persistence.LoadDefaultWorld();
      }
    }
  }
}