namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing
{
  /// <summary>
  /// The area a <see cref="CubusEditToolDefinition"/> affects around the targeted point.
  /// </summary>
  public enum EditToolShape
  {
    /// <summary>A single voxel (block layer) or a minimal brush (density layer).</summary>
    Point,

    /// <summary>A sphere of <c>range</c> voxels radius.</summary>
    Sphere,
  }
}
