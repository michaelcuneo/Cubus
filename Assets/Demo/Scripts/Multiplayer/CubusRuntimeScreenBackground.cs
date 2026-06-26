using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Disabled while launcher/loading UI is owned directly by the screen controllers.
  /// Keeping this as a no-op avoids deleting references while preventing another
  /// runtime-generated Canvas/background layer from masking UI during startup.
  /// </summary>
  public sealed class CubusRuntimeScreenBackground : MonoBehaviour
  {
  }
}
