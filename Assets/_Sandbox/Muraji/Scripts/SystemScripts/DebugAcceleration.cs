using UnityEngine;
using TMPro;

public class DebugAcceleration : MonoBehaviour
{
    [SerializeField] private ChainsawAccelerator chainsawAccelerator;
    [SerializeField] private TMP_Text tMP_Text;
        // Update is called once per frame
    void Update()
    {
        tMP_Text.text = $"Speed: {chainsawAccelerator.CurrentSpeed:F2}";
    }
}
