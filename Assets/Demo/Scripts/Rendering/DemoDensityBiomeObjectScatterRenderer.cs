using UnityEngine;

namespace Assets.Demo.Scripts.Rendering
{
  [DisallowMultipleComponent]
  public sealed class DemoDensityBiomeObjectScatterRenderer : MonoBehaviour
  {
    // Density object scatter is intentionally disabled while it is redesigned.
    // The previous implementation synchronously scanned every density triangle
    // and resolved a biome for each candidate during chunk streaming, which
    // could stall initial world