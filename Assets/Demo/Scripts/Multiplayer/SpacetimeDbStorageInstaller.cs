using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using UnityEngine;
using Assets.Demo.Scripts.Core;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Registers the SpacetimeDB-backed chunk store factory with <see cref="CubusWorldStorage"/> before
  /// any scene component awakes. When a scene's <c>CubusWorldStorage</c> uses the
  /// <see cref="WorldStorageBackend.SpacetimeDb"/> backend it will build a
  /// <see cref="SpacetimeDbWorldChunkStore"/> bound to the active <see cref="DemoNetworkManager"/>.
  /// </summary>
  public static class SpacetimeDbStorageInstaller
  {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
      CubusWorldStorage.ExternalStoreFactory = CreateStore;
    }

    private static IWorldChunkStore CreateStore(CubusWorldStorage storage)
    {
      DemoNetworkManager net = DemoNetworkManager.Instance;
      if (net == null)
      {
        Debug.LogWarning(
            "[CubusNetwork] SpacetimeDb storage backend is active but no DemoNetworkManager exists in the scene. " +
            "Falling back to local file storage.");
        return null;
      }

      // Keep the network manager's world id aligned with the storage component so
      // chunk/edit subscriptions target the same world.
      net.WorldId = storage.WorldId;

      // A Connected (server) world and a Local world with the SAME id are DIFFERENT worlds.
      // Cache the server world's base chunks under a separate root so a local single-player
      // save and a shared server save never collide on disk (Local == Connected symptom).
      string worldsSubfolder =
          DemoGameLaunchContext.HasLaunch && DemoGameLaunchContext.Mode == DemoGameLaunchMode.Connected
              ? "RemoteWorlds"
              : "Worlds";

      return new SpacetimeDbWorldChunkStore(net, storage.WorldId, worldsSubfolder);
    }
  }
}
