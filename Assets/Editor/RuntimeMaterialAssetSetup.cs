using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class RuntimeMaterialAssetSetup
{
    private const string ResourceRoot = "Assets/Resources";
    private const string MaterialFolder = ResourceRoot + "/RuntimeMaterials";
    private const string OpaqueMaterialPath = MaterialFolder + "/RuntimeOpaque.mat";
    private const string TransparentMaterialPath = MaterialFolder + "/RuntimeTransparent.mat";
    private const string ShaderName = "Universal Render Pipeline/Unlit";

    [MenuItem("Tools/Logo Survivor/Ensure Runtime WebGL Materials")]
    public static void EnsureRuntimeMaterials()
    {
        EnsureFolder(ResourceRoot, MaterialFolder);

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            throw new System.InvalidOperationException($"Required shader was not found: {ShaderName}");
        }

        ConfigureMaterial(OpaqueMaterialPath, shader, transparent: false);
        ConfigureMaterial(TransparentMaterialPath, shader, transparent: true);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Runtime WebGL materials are ready: {OpaqueMaterialPath}, {TransparentMaterialPath}");
    }

    private static void EnsureFolder(string parent, string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string folderName = Path.GetFileName(folder);
        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static void ConfigureMaterial(string path, Shader shader, bool transparent)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }

        material.name = Path.GetFileNameWithoutExtension(path);
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        material.SetFloat("_Surface", transparent ? 1f : 0f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
        material.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
        material.SetFloat("_ZWrite", transparent ? 0f : 1f);
        material.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
        material.renderQueue = transparent ? (int)RenderQueue.Transparent : -1;

        if (transparent)
        {
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        else
        {
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        EditorUtility.SetDirty(material);
    }
}
