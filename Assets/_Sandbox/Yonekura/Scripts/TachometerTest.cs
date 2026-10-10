using UnityEngine;

public class TachometerTest : MonoBehaviour
{
    [SerializeField] private NeedleView needleView;
    [SerializeField] private float testValue = 0f;

    private void Update()
    {
        needleView.SetValue(testValue);
    }
}
