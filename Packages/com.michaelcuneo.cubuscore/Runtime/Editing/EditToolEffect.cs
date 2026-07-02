namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing
{
  /// <summary>
  /// The terrain effect a <see cref="CubusEditToolDefinition"/> performs. The first two
  /// act on the block layer; the rest sculpt the smooth density layer.
  /// </summary>
  public enum EditToolEffect
  {
    PlaceBlock,
    RemoveBlock,
    Raise,
    Lower,
    Smooth,
    Flatten,
  }
}
