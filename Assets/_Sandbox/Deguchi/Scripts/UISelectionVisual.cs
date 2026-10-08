using UnityEngine;
using UnityEngine.EventSystems;

public class UISelectionVisual :
    MonoBehaviour,
    ISelectHandler,
    IDeselectHandler
{
    [Header("Selection Visual")]
    [SerializeField]
    private GameObject selectionMark;

    [SerializeField]
    private GameObject selectionBackground;

    private void Awake()
    {
        SetSelected(false);
    }

    private void OnEnable()
    {
        bool isSelected =
            EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject
                == gameObject;

        SetSelected(isSelected);
    }

    public void OnSelect(BaseEventData eventData)
    {
        SetSelected(true);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        SetSelected(false);
    }

    private void SetSelected(bool selected)
    {
        if (selectionMark != null)
        {
            selectionMark.SetActive(selected);
        }

        if (selectionBackground != null)
        {
            selectionBackground.SetActive(selected);
        }
    }
}