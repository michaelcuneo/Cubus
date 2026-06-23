using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod;
using NUnit.Framework;
using UnityEngine;

namespace CubusCore.Tests
{
  /// <summary>
  /// Guards <see cref="LodTileKey"/> value semantics: the LOD renderer/streamer
  /// keys dictionaries by (level, coord), so equal keys must hash equally and a
  /// tile at the same coordinate but a different level must be a distinct key.
  /// </summary>
  [TestFixture]
  public sealed class LodTileKeyTests
  {
    [Test]
    public void EqualKeys_AreEqualAndShareHash()
    {
      var a = new LodTileKey(2, new Vector3Int(1, -3, 4));
      var b = new LodTileKey(2, new Vector3Int(1, -3, 4));

      Assert.AreEqual(a, b, "Keys with the same level and coord must be equal.");
      Assert.AreEqual(a.GetHashCode(), b.GetHashCode(), "Equal keys must hash equally.");
    }

    [Test]
    public void SameCoordDifferentLevel_AreDistinct()
    {
      var a = new LodTileKey(1, new Vector3Int(5, 0, 5));
      var b = new LodTileKey(2, new Vector3Int(5, 0, 5));

      Assert.AreNotEqual(a, b, "Same coordinate at different LOD levels must be distinct keys.");
    }

    [Test]
    public void UsableAsDictionaryKey()
    {
      var map = new Dictionary<LodTileKey, int>
      {
        [new LodTileKey(3, new Vector3Int(0, 0, 0))] = 7
      };

      Assert.IsTrue(map.ContainsKey(new LodTileKey(3, new Vector3Int(0, 0, 0))), "Lookup by an equal key must succeed.");
      Assert.IsFalse(map.ContainsKey(new LodTileKey(3, new Vector3Int(0, 1, 0))), "A different coord must not match.");
    }
  }
}
