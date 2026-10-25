using System.Collections.Generic;
using UnityEngine;

// Every townsperson is drawn with ONE shared material: URP Lit reading its colour from a small palette texture,
// one texel per colour. A character's mesh points each face's UVs at the texel of its colour (NpcBody), so the
// whole population - any outfit, any hair - is a single material and one draw call per character.
public static class NpcPalette
{
    private const int Size = 64; // 4096 colours - far more than the city ever uses
    private static Texture2D texture;
    private static Material material;
    private static readonly Dictionary<Color32, int> slots = new Dictionary<Color32, int>();
    private static bool dirty;

    public static Material Material
    {
        get
        {
            Ensure();
            if (dirty) { texture.Apply(false); dirty = false; }
            return material;
        }
    }

    private static void Ensure()
    {
        if (texture != null && material != null) return;
        texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "NpcPalette", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var clear = new Color32[Size * Size];
        for (int i = 0; i < clear.Length; i++) clear[i] = new Color32(255, 0, 255, 255);
        texture.SetPixels32(clear);
        slots.Clear();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        material = new Material(shader) { name = "NpcPaletteLit", enableInstancing = true };
        material.SetTexture("_BaseMap", texture);
        material.mainTexture = texture;
        material.color = Color.white;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
        dirty = true;
    }

    // The UV of this colour's texel (added to the palette the first time it's asked for).
    public static Vector2 UV(Color c)
    {
        Ensure();
        Color32 key = c;
        key.a = 255;
        if (!slots.TryGetValue(key, out int slot))
        {
            slot = slots.Count;
            if (slot >= Size * Size) slot = Size * Size - 1;
            slots[key] = slot;
            texture.SetPixel(slot % Size, slot / Size, key);
            dirty = true;
        }
        return new Vector2((slot % Size + 0.5f) / Size, (slot / Size + 0.5f) / Size);
    }

    public static int ColorsUsed => slots.Count;
}
