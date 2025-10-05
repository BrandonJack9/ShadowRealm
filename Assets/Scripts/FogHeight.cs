#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

[ExecuteAlways]
public class SharedShaderFloatEditor : MonoBehaviour
{
    public float value = 1.0f;
    public string shaderPropertyName = "_Global_Fog_Height";

    void OnValidate()
    {
        Shader.SetGlobalFloat(shaderPropertyName, value);
    }
}