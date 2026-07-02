using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing
{
  /// <summary>
  /// A data-driven terrain-editing tool description. The engine does NOT hard-code any tools:
  /// a game defines its own (pickaxe, shovel, trowel, steam roller, ...) as instances of this
  /// type — inline on a component, inside a ScriptableObject, or attached to an inventory item —
  /// and hands them to <see cref="CubusEditController.Apply(CubusEditToolDefinition, Ray)"/> when
  /// used. Activation, input and inventory ownership all live outside the voxel engine.
  /// </summary>
  [System.Serializable]
  public sealed class CubusEditToolDefinition
  {
    [Tooltip("Human-readable name, e.g. Pickaxe / Shovel / Trowel / Steam Roller.")]
    public string displayName = "Tool";

    [Tooltip("What the tool does to the terrain.")]
    public EditToolEffect effect = EditToolEffect.RemoveBlock;

    [Tooltip("Area affected around the targeted point.")]
    public EditToolShape shape = EditToolShape.Point;

    [Tooltip("Radius in voxels for Sphere shape and all density brushes.")]
    [Min(0.0f)]
    public float range = 4.0f;

    [Tooltip("Density delta for Raise/Lower; 0..1 blend for Smooth/Flatten. Ignored by block effects.")]
    [Min(0.0f)]
    public float strength = 2.5f;

    [Tooltip("Material applied by PlaceBlock and Raise.")]
    public ushort materialId = 1;

    [Tooltip("Optional suggested hotkey a driver/inventory may use to select this tool. The engine does not read input itself.")]
    public KeyCode activationKey = KeyCode.None;

    [Tooltip("Whether the tool is meant to keep applying while held (dig/build) versus a single hit.")]
    public bool continuous = false;

    [Tooltip("Minimum seconds between applications while held, for continuous tools.")]
    [Min(0.0f)]
    public float repeatInterval = 0.08f;

    /// <summary>True when this tool edits the block layer (PlaceBlock / RemoveBlock).</summary>
    public bool EditsBlockLayer =>
        effect == EditToolEffect.PlaceBlock || effect == EditToolEffect.RemoveBlock;
  }
}
