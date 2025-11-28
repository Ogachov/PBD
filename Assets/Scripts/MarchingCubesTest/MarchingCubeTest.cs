using UnityEngine;

public class MarchingCubeTest : MonoBehaviour
{
    [SerializeField]private GameObject CubePrefab;
    [SerializeField]private GameObject LinePrefab;

    private int[] Cells = new int[11 * 11 * 11];
    
    

    void Start()
    {
#if false
        for (var i = 0; i <= 10; i++)
        {
            for (var j = 0; j <= 10; j++)
            {
                var go = Instantiate(LinePrefab, new Vector3(j - 5, 5, i - 5), Quaternion.identity);
                go.transform.localScale = new Vector3(0.04f, 10f, 0.04f);
                go.transform.parent = transform;
                
                go = Instantiate(LinePrefab, new Vector3(j - 5, i, 0), Quaternion.identity);
                go.transform.localScale = new Vector3(0.04f, 0.04f, 10f);
                go.transform.parent = transform;
                
                go = Instantiate(LinePrefab, new Vector3(0, j, i - 5), Quaternion.identity);
                go.transform.localScale = new Vector3(10f, 0.04f, 0.04f);
                go.transform.parent = transform;
            }
        }
#endif
        for (var z = 1; z <= 9; z++)
        {
            for (var y = 1; y <= 9; y++)
            {
                for (var x = 1; x <= 9; x++)
                {
                    var noiseScale = 0.1f;
                    var value = Perlin.Noise(x * noiseScale + 0.5f, y * noiseScale + 0.5f, z * noiseScale + 0.5f);
                    if (value < 0.1f)
                    {
                        Cells[x + y * 11 + z * 11 * 11] = 0;
                    }
                    else
                    {
                        Cells[x + y * 11 + z * 11 * 11] = 1;
                        var go = Instantiate(CubePrefab, new Vector3(x - 5, z, y - 5), Quaternion.identity);
                        go.transform.parent = transform;
                    }
                }
            }
        }
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
