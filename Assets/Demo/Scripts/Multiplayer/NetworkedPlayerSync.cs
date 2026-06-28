using System.Collections.Generic;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;
using PlayerRow = SpacetimeDB.Types.Player;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Pushes the local player's transform to the server on an interval and renders every other
  /// connected player as an interpolated avatar driven by the replicated <c>player</c> table.
  /// </summary>
  public sealed class NetworkedPlayerSync : MonoBehaviour
  {
    [Header("Local Player")]
    [Tooltip("Transform whose position + yaw is replicated. Defaults to this GameObject.")]
    [SerializeField] private Transform localPlayer;

    [Tooltip("Optional camera/head used for pitch. Defaults to the first child camera.")]
    [SerializeField] private Transform localPitchSource;

    [Header("Networking")]
    [Tooltip("Seconds between transform updates sent to the server.")]
    [SerializeField] private float sendInterval = 0.1f;

    [Tooltip("Minimum movement (metres) before an update is sent.")]
    [SerializeField] private float positionThreshold = 0.02f;

    [Header("Remote Avatars")]
    [Tooltip("Optional prefab for remote players. A capsule is generated when empty.")]
    [SerializeField] private GameObject remoteAvatarPrefab;

    [SerializeField] private float interpolationSpeed = 12.0f;
    [SerializeField] private bool showNameLabels = true;

    private sealed class RemoteAvatar
    {
      public GameObject Root;
      public Vector3 TargetPosition;
      public Quaternion TargetRotation;
      public TextMesh Label;
    }

    private readonly Dictionary<Identity, RemoteAvatar> avatars = new();

    private DemoNetworkManager net;
    private float sendTimer;
    private Vector3 lastSentPosition;
    private float lastSentYaw;
    private float lastSentPitch;
    private bool callbacksRegistered;

    private void Awake()
    {
      if (localPlayer == null)
      {
        localPlayer = transform;
      }

      if (localPitchSource == null)
      {
        Camera childCamera = GetComponentInChildren<Camera>();
        if (childCamera != null)
        {
          localPitchSource = childCamera.transform;
        }
      }
    }

    private void OnEnable()
    {
      net = DemoNetworkManager.Instance;
      if (net != null)
      {
        net.Connected += HandleConnected;
        net.Disconnected += HandleDisconnected;
        if (net.IsConnected)
        {
          HandleConnected(net.Conn, net.LocalIdentity);
        }
      }
    }

    private void OnDisable()
    {
      if (net != null)
      {
        net.Connected -= HandleConnected;
        net.Disconnected -= HandleDisconnected;
      }
      UnregisterCallbacks();
      ClearAvatars();
    }

    private void HandleConnected(DbConnection conn, Identity identity)
    {
      RegisterCallbacks(conn);
    }

    private void HandleDisconnected()
    {
      UnregisterCallbacks();
      ClearAvatars();
    }

    private void RegisterCallbacks(DbConnection conn)
    {
      if (callbacksRegistered || conn == null)
      {
        return;
      }

      conn.Db.Player.OnInsert += HandlePlayerInsert;
      conn.Db.Player.OnUpdate += HandlePlayerUpdate;
      conn.Db.Player.OnDelete += HandlePlayerDelete;
      callbacksRegistered = true;

      // Spawn anyone already present in the local cache.
      foreach (PlayerRow player in conn.Db.Player.Iter())
      {
        UpsertAvatar(player);
      }
    }

    private void UnregisterCallbacks()
    {
      if (!callbacksRegistered || net == null || net.Conn == null)
      {
        callbacksRegistered = false;
        return;
      }

      net.Conn.Db.Player.OnInsert -= HandlePlayerInsert;
      net.Conn.Db.Player.OnUpdate -= HandlePlayerUpdate;
      net.Conn.Db.Player.OnDelete -= HandlePlayerDelete;
      callbacksRegistered = false;
    }

    private void HandlePlayerInsert(EventContext ctx, PlayerRow row) => UpsertAvatar(row);

    private void HandlePlayerUpdate(EventContext ctx, PlayerRow oldRow, PlayerRow newRow) => UpsertAvatar(newRow);

    private void HandlePlayerDelete(EventContext ctx, PlayerRow row) => RemoveAvatar(row.Identity);

    private bool IsLocal(Identity identity) => net != null && identity == net.LocalIdentity;

    private void UpsertAvatar(PlayerRow player)
    {
      if (IsLocal(player.Identity))
      {
        return;
      }

      if (!player.Online)
      {
        RemoveAvatar(player.Identity);
        return;
      }

      if (!avatars.TryGetValue(player.Identity, out RemoteAvatar avatar))
      {
        avatar = CreateAvatar(player);
        avatars[player.Identity] = avatar;
      }

      avatar.TargetPosition = new Vector3(player.X, player.Y, player.Z);
      avatar.TargetRotation = Quaternion.Euler(0.0f, player.Yaw, 0.0f);

      if (avatar.Label != null)
      {
        avatar.Label.text = player.Name;
      }
    }

    private RemoteAvatar CreateAvatar(PlayerRow player)
    {
      GameObject root = remoteAvatarPrefab != null
          ? Instantiate(remoteAvatarPrefab)
          : GameObject.CreatePrimitive(PrimitiveType.Capsule);

      root.name = $"RemotePlayer_{player.Name}";
      root.transform.position = new Vector3(player.X, player.Y, player.Z);

      // Avatars are visual only; never block raycasts or movement.
      foreach (Collider col in root.GetComponentsInChildren<Collider>())
      {
        col.enabled = false;
      }

      ApplyColor(root, player.ColorRgba);

      TextMesh label = null;
      if (showNameLabels)
      {
        label = CreateLabel(root.transform, player.Name);
      }

      return new RemoteAvatar
      {
        Root = root,
        TargetPosition = new Vector3(player.X, player.Y, player.Z),
        TargetRotation = Quaternion.Euler(0.0f, player.Yaw, 0.0f),
        Label = label,
      };
    }

    private static void ApplyColor(GameObject root, uint rgba)
    {
      Color color = new(
          ((rgba >> 24) & 0xFF) / 255.0f,
          ((rgba >> 16) & 0xFF) / 255.0f,
          ((rgba >> 8) & 0xFF) / 255.0f,
          1.0f);

      Renderer renderer = root.GetComponentInChildren<Renderer>();
      if (renderer != null)
      {
        renderer.material.color = color;
      }
    }

    private static TextMesh CreateLabel(Transform parent, string text)
    {
      GameObject labelObject = new("NameLabel");
      labelObject.transform.SetParent(parent, false);
      labelObject.transform.localPosition = new Vector3(0.0f, 1.4f, 0.0f);
      labelObject.transform.localScale = Vector3.one * 0.1f;

      TextMesh mesh = labelObject.AddComponent<TextMesh>();
      mesh.text = text;
      mesh.anchor = TextAnchor.LowerCenter;
      mesh.alignment = TextAlignment.Center;
      mesh.fontSize = 48;
      mesh.color = Color.white;
      mesh.characterSize = 0.5f;
      return mesh;
    }

    private void RemoveAvatar(Identity identity)
    {
      if (avatars.TryGetValue(identity, out RemoteAvatar avatar))
      {
        if (avatar.Root != null)
        {
          Destroy(avatar.Root);
        }
        avatars.Remove(identity);
      }
    }

    private void ClearAvatars()
    {
      foreach (RemoteAvatar avatar in avatars.Values)
      {
        if (avatar.Root != null)
        {
          Destroy(avatar.Root);
        }
      }
      avatars.Clear();
    }

    private void Update()
    {
      InterpolateAvatars();
      SendLocalTransform();
    }

    private void InterpolateAvatars()
    {
      float t = 1.0f - Mathf.Exp(-interpolationSpeed * Time.deltaTime);
      Camera mainCamera = Camera.main;

      foreach (RemoteAvatar avatar in avatars.Values)
      {
        if (avatar.Root == null)
        {
          continue;
        }

        avatar.Root.transform.position = Vector3.Lerp(avatar.Root.transform.position, avatar.TargetPosition, t);
        avatar.Root.transform.rotation = Quaternion.Slerp(avatar.Root.transform.rotation, avatar.TargetRotation, t);

        if (avatar.Label != null && mainCamera != null)
        {
          avatar.Label.transform.rotation = Quaternion.LookRotation(
              avatar.Label.transform.position - mainCamera.transform.position);
        }
      }
    }

    private void SendLocalTransform()
    {
      if (net == null || !net.IsConnected || !net.IsSubscriptionApplied || localPlayer == null)
      {
        return;
      }

      sendTimer += Time.deltaTime;
      if (sendTimer < sendInterval)
      {
        return;
      }
      sendTimer = 0.0f;

      Vector3 position = localPlayer.position;
      float yaw = localPlayer.eulerAngles.y;
      float pitch = localPitchSource != null ? NormalizePitch(localPitchSource.eulerAngles.x) : 0.0f;

      bool moved = (position - lastSentPosition).sqrMagnitude >= positionThreshold * positionThreshold;
      bool turned = !Mathf.Approximately(yaw, lastSentYaw) || !Mathf.Approximately(pitch, lastSentPitch);
      if (!moved && !turned)
      {
        return;
      }

      lastSentPosition = position;
      lastSentYaw = yaw;
      lastSentPitch = pitch;

      net.Conn.Reducers.UpdatePlayerTransform(position.x, position.y, position.z, yaw, pitch);
    }

    private static float NormalizePitch(float pitch)
    {
      return pitch > 180.0f ? pitch - 360.0f : pitch;
    }
  }
}
