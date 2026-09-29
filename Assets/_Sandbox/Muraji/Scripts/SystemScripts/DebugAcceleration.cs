using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class DebugAcceleration : MonoBehaviour
{
    [SerializeField] private ChainsawAccelerator chainsawAccelerator;
    [SerializeField] private Text TextAcceleration;
        // Update is called once per frame
    void Update()
    {
        TextAcceleration.text = $"Speed: {chainsawAccelerator.CurrentSpeed:F2}";
    }
}
