using UnityEngine;

public class NeedleView : MonoBehaviour
{
    [SerializeField] private float maxValue = 100f;

    [SerializeField] private float minAngle = 120f;
    [SerializeField] private float maxAngle = -120f;

    private RectTransform needle;

    private void Awake()
    {
        needle = GetComponent<RectTransform>();
    }

    public void SetValue(float value)
    {
        float normalized = Mathf.Clamp01(value / maxValue);

        float angle = Mathf.Lerp(
            minAngle,
            maxAngle,
            normalized
        );

        needle.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
}
