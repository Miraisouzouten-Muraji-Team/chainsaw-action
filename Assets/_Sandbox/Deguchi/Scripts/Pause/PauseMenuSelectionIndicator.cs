using UnityEngine;
using UnityEngine.EventSystems;

public class PauseMenuSelectionIndicator :
    MonoBehaviour,
    ISelectHandler,
    IDeselectHandler
{
    [SerializeField]
    private GameObject selectionMark;

    private void Awake()
    {
        if (selectionMark != null)
        {
            selectionMark.SetActive(false);
        }
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (selectionMark != null)
        {
            selectionMark.SetActive(true);
        }
    }

    public void OnDeselect(BaseEventData eventData)
    {
        if (selectionMark != null)
        {
            selectionMark.SetActive(false);
        }
    }
}