using UnityEngine;

public class FPS : MonoBehaviour
{
    private TMPro.TextMeshProUGUI text;
    private float _fps;
    
    void Start()
    {
        text = GetComponent<TMPro.TextMeshProUGUI>();
    }

    void Update()
    {
        _fps = (_fps * 9f + 1.0f / Time.deltaTime) / 10f;
        text.text = $"FPS: {_fps:0.00}";
    }
}
