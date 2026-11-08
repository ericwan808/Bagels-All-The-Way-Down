using System.IO;
using Bagels.Core;
using Bagels.Data;
using Bagels.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the test-set content: generated sprites, pastry/formula/level assets, the Pastry prefab, and the Game
/// object in the main scene. Idempotent: re-running updates the existing assets in place.
/// </summary>
public static class BagelsSetup
{
    const string SpriteDir = "Assets/Sprites/Generated";
    const string DataDir = "Assets/Data/TestSet";
    const string PrefabPath = "Assets/Prefabs/Pastry.prefab";
    const string ScenePath = "Assets/Scenes/main.unity";
    const string CirclePath = "Assets/Sprites/Circle.png";

    [MenuItem("Bagels/Setup Test Level")]
    public static void Run()
    {
        var circle = AssetDatabase.LoadAssetAtPath<Sprite>(CirclePath);
        var ring = MakeSprite("Ring", 256, 256, RingAlpha);
        var triangle = MakeSprite("Triangle", 128, 128, TriangleAlpha);
        var square = MakeSprite("Square", 8, 8, (x, y, n) => 1f);

        var level = MakeLevel();
        var prefab = MakePrefab(circle);
        SetupScene(level, prefab, circle, ring, square, triangle);

        AssetDatabase.SaveAssets();
        Debug.Log("Bagels: test level, prefab and scene set up.");
    }

    // ------------------------------------------------------------------ data

    static LevelAsset MakeLevel()
    {
        EnsureDir(DataDir);
        var root = Pastry("Root", "Root Bread", Symbol.R, new Color(0.93f, 0.85f, 0.72f), LayoutKind.W7);
        var a = Pastry("A", "A", Symbol.A, new Color(1f, 0.93f, 0.72f), LayoutKind.None);
        var b = Pastry("B", "B", Symbol.B, new Color(0.96f, 0.75f, 0.40f), LayoutKind.None);
        var c = Pastry("C", "C (bread)", Symbol.C, new Color(0.86f, 0.68f, 0.46f), LayoutKind.K3);
        var d = Pastry("D", "D", Symbol.D, new Color(0.78f, 0.42f, 0.22f), LayoutKind.None);
        var e = Pastry("E", "E", Symbol.E, new Color(0.45f, 0.24f, 0.13f), LayoutKind.None);

        var fb = Formula("Formula B", b, new[] { (a, false), (a, false) }, (0, 1, AdjacencyKind.Normal));
        var fc = Formula("Formula C", c, new[] { (b, false), (b, false) }, (0, 1, AdjacencyKind.Normal));
        var fd = Formula("Formula D", d, new[] { (a, false), (a, false), (b, false) },
                         (0, 1, AdjacencyKind.Normal), (1, 2, AdjacencyKind.Normal), (0, 2, AdjacencyKind.Normal));
        var fe = Formula("Formula E", e, new[] { (b, true), (d, false), (d, false) },
                         (0, 1, AdjacencyKind.Down), (0, 2, AdjacencyKind.Down));

        var level = Load<LevelAsset>($"{DataDir}/Test Set.asset");
        // Most specific first: D's final A would otherwise be taken by A+A->B.
        level.EditorInit(root, e, a, new[] { fd, fe, fc, fb });
        EditorUtility.SetDirty(level);
        return level;
    }

    static PastryType Pastry(string file, string display, Symbol symbol, Color color, LayoutKind layout)
    {
        var p = Load<PastryType>($"{DataDir}/Pastry {file}.asset");
        p.EditorInit(display, symbol, color, layout);
        EditorUtility.SetDirty(p);
        return p;
    }

    static FormulaAsset Formula(string file, PastryType product, (PastryType type, bool catalyst)[] ingredients,
                                params (int from, int to, AdjacencyKind kind)[] links)
    {
        var f = Load<FormulaAsset>($"{DataDir}/{file}.asset");
        var nodes = new FormulaAsset.Ingredient[ingredients.Length];
        for (int i = 0; i < nodes.Length; i++)
            nodes[i] = new FormulaAsset.Ingredient { type = ingredients[i].type, catalyst = ingredients[i].catalyst };
        var edges = new FormulaAsset.Link[links.Length];
        for (int i = 0; i < edges.Length; i++)
            edges[i] = new FormulaAsset.Link { from = links[i].from, to = links[i].to, kind = links[i].kind };
        f.EditorInit(product, nodes, edges);
        EditorUtility.SetDirty(f);
        return f;
    }

    static T Load<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    // ------------------------------------------------------------------ prefab

    static PastryView MakePrefab(Sprite circle)
    {
        EnsureDir(Path.GetDirectoryName(PrefabPath));
        var go = new GameObject("Pastry");
        var body = go.AddComponent<SpriteRenderer>();
        body.sprite = circle;

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(go.transform, false);
        var mr = labelGo.AddComponent<MeshRenderer>();
        var label = labelGo.AddComponent<TextMesh>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        mr.sharedMaterial = label.font.material;
        label.text = "A";
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.fontSize = 128;
        label.characterSize = 0.55f / 12.8f;
        label.fontStyle = FontStyle.Bold;

        var view = go.AddComponent<PastryView>();
        view.EditorInit(body, label);

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);
        return prefab.GetComponent<PastryView>();
    }

    // ------------------------------------------------------------------ scene

    static void SetupScene(LevelAsset level, PastryView prefab, Sprite circle, Sprite ring, Sprite square, Sprite triangle)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Remove the earlier hand-built prototype objects.
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Bread" || root.name == "Bagel")
                Object.DestroyImmediate(root);

        GameView game = Object.FindFirstObjectByType<GameView>();
        if (game == null) game = new GameObject("Game").AddComponent<GameView>();
        game.transform.position = Vector3.zero;
        game.EditorInit(level, prefab, circle, ring, square, triangle);
        EditorUtility.SetDirty(game);

        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.98f, 0.95f, 0.9f);
            EditorUtility.SetDirty(cam);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // ------------------------------------------------------------------ sprites

    delegate float AlphaFn(float x, float y, int size);

    static Sprite MakeSprite(string name, int size, float pixelsPerUnit, AlphaFn alpha)
    {
        EnsureDir(SpriteDir);
        string path = $"{SpriteDir}/{name}.png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color32(255, 255, 255,
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f, size)) * 255f));
        tex.SetPixels32(pixels);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // Anti-aliased ring: outer edge at the texture border, thickness 8% of the diameter.
    static float RingAlpha(float x, float y, int size)
    {
        float c = size * 0.5f;
        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
        float outer = c - 1f;
        float inner = outer - size * 0.08f;
        return Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);
    }

    // Triangle pointing right (+x), filling the texture.
    static float TriangleAlpha(float x, float y, int size)
    {
        float u = x / size, v = y / size;
        float halfWidth = 0.5f * (1f - u); // edges run from (0,0)/(0,1) to the tip at (1,0.5)
        float dist = halfWidth - Mathf.Abs(v - 0.5f);
        return Mathf.Clamp01(dist * size + 0.5f);
    }

    static void EnsureDir(string dir)
    {
        if (!AssetDatabase.IsValidFolder(dir))
        {
            var parent = Path.GetDirectoryName(dir).Replace('\\', '/');
            EnsureDir(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
        }
    }
}
