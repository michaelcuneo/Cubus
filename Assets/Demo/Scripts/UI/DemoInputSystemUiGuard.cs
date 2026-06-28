using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Assets.Demo.Scripts.UI
{
  [DefaultExecutionOrder(-32000)]
  public sealed class DemoInputSystemUiGuard : MonoBehaviour
  {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
      if (FindAnyObjectByType<DemoInputSystemUiGuard>() != null)
      {
        return;
      }

      GameObject go = new("Demo Input System UI Guard");
      go.AddComponent<DemoInputSystemUiGuard>();
      DontDestroyOnLoad(go);
    }

    private void Awake()
    {
      FixAllEventSystems();
    }

    private void Update()
    {
      FixAllEventSystems();
    }

    private void LateUpdate()
    {
      FixAllEventSystems();
    }

    public static void FixAllEventSystems()
    {
      EventSystem[] eventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
      for (int i = 0; i < eventSystems.Length; i++)
      {
        EventSystem eventSystem = eventSystems[i];
        if (eventSystem == null)
        {
          continue;
        }

        StandaloneInputModule legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
        if (legacyModule != null)
        {
          Destroy(legacyModule);
        }

        if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
        {
          eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        }
      }
    }

    public static EventSystem EnsureEventSystem()
    {
      EventSystem eventSystem = EventSystem.current != null
          ? EventSystem.current
          : FindAnyObjectByType<EventSystem>();

      if (eventSystem == null)
      {
        GameObject go = new("EventSystem");
        eventSystem = go.AddComponent<EventSystem>();
        DontDestroyOnLoad(go);
      }

      FixAllEventSystems();
      return eventSystem;
    }
  }
}
