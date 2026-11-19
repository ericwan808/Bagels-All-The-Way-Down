using System.Collections.Generic;
using System.IO;
using Bagels.Core;
using Bagels.Data;
using Bagels.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Builds the test-set content: generated sprites, pastry/formula/level assets, the Pastry prefab, and the main
/// scene's Game object with its side panel and an EventSystem. Idempotent: re-running updates everything in place.
/// The board itself is not saved; <see cref="GameView"/> draws it as an edit-mode preview.
/// </summary>
public static class BagelsSetup
{
    const string SpriteDir = "Assets/Sprites/Generated";
    const string DataDir = "Assets/Data/TestSet";
    const string PrefabPath = "Assets/Prefabs/Pastry.prefab";
    const string ScenePath = "Assets/Scenes/main.unity";
    const string TitleScenePath = "Assets/Scenes/title.unity";
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

    /// <summary>
    /// (Re)creates the title scene, which holds only a <see cref="TitleScreen"/> (it builds its UI at runtime), and
    /// lists it first in the build settings so the game starts on it.
    /// </summary>
    [MenuItem("Bagels/Setup Title Scene")]
    public static void SetupTitleScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Title Screen").AddComponent<TitleScreen>();
        EditorSceneManager.SaveScene(scene, TitleScenePath);

        var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(TitleScenePath, true) };
        foreach (var s in EditorBuildSettings.scenes)
            if (s.path != TitleScenePath) scenes.Add(s);
        if (!scenes.Exists(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log("Bagels: title scene set up and listed first in the build settings.");
    }

    /// <summary>Rebuilds the saved side panel in the open scene, e.g. after editing formulas or pastry types.</summary>
    [MenuItem("Bagels/Rebuild Side Panel")]
    public static void RebuildPanel()
    {
        var game = Object.FindFirstObjectByType<GameView>();
        if (game == null) { Debug.LogError("Bagels: no GameView in the open scene. Run Bagels/Setup Test Level."); return; }
        BakeUi(game);
        EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
        Debug.Log("Bagels: side panel rebuilt. Save the scene to keep it.");
    }

    /// <summary>Redraws the edit-mode board preview (it also redraws on its own when the scene loads or GameView changes).</summary>
    [MenuItem("Bagels/Refresh Board Preview")]
    public static void RefreshPreview()
    {
        foreach (var game in Object.FindObjectsByType<GameView>(FindObjectsSortMode.None)) game.RefreshPreview();
    }

    static void BakeUi(GameView game)
    {
        if (game.RebuildPanel() == null) Debug.LogError("Bagels: GameView has no level, so no side panel was built.", game);
        var es = SidePanel.EnsureEventSystem();
        var module = es.GetComponent<InputSystemUIInputModule>();
        // In-memory default actions would be saved as a missing reference; with none, the module assigns its
        // defaults when it starts at runtime.
        if (module != null && module.actionsAsset != null && !EditorUtility.IsPersistent(module.actionsAsset))
            module.UnassignActions();
        EditorUtility.SetDirty(es.gameObject);
    }

    // ------------------------------------------------------------------ data

    static LevelAsset MakeLevel()
    {
        EnsureDir(DataDir);
        // The design.md formula list: name, price (null = craft only) and formula, in product order, which is also
        // the unlock order. A, B and C are known from the start. In orders, a C may hold
        // only bagels, an E two levels and an H three. A C holds one of a few topping sets; an E at most one G or I
        // and nothing after I, and an H at most one J or K, counting everything inside them. G and after are
        // high-level: one layer of an order holds only a few of them.
        var root = Pastry("Root", "Root Bread", Symbol.R, new Color(0.93f, 0.85f, 0.72f), LayoutKind.W7, null, true);
        var a = Pastry("A", "Everything bagel", Symbol.A, new Color(1f, 0.93f, 0.72f), LayoutKind.None, 0, true);
        var b = Pastry("B", "Everything bagel + Cream cheese", Symbol.B, new Color(0.96f, 0.75f, 0.40f), LayoutKind.None, 2, true);
        var c = Pastry("C", "Croissant", Symbol.C, new Color(0.86f, 0.68f, 0.46f), LayoutKind.K3, 10, true, orderLevels: 1);
        var d = Pastry("D", "Everything everything bagel", Symbol.D, new Color(0.78f, 0.42f, 0.22f), LayoutKind.None, 25);
        var e = Pastry("E", "Baguette", Symbol.E, new Color(0.45f, 0.24f, 0.13f), LayoutKind.K23, 100, orderLevels: 2);
        var f = Pastry("F", "Dharma bagel", Symbol.F, new Color(0.62f, 0.80f, 0.55f), LayoutKind.None, 888);
        var g = Pastry("G", "Earth bagel", Symbol.G, new Color(0.40f, 0.62f, 0.78f), LayoutKind.None, 1970);
        var h = Pastry("H", "Slice of wonderbread", Symbol.H, new Color(0.74f, 0.58f, 0.82f), LayoutKind.W7, 11500, orderLevels: 3);
        var i = Pastry("I", "Black hole bagel", Symbol.I, new Color(0.30f, 0.22f, 0.36f), LayoutKind.None, 197800);
        var j = Pastry("J", "Galaxy bagel", Symbol.J, new Color(0.88f, 0.52f, 0.62f), LayoutKind.None, 4297000);
        var k = Pastry("K", "Bagel on a turtle on a bagel on a turtle", Symbol.K, new Color(0.45f, 0.70f, 0.45f), LayoutKind.None, null);
        var l = Pastry("L", "True plain bagel", Symbol.L, new Color(0.98f, 0.84f, 0.25f), LayoutKind.None, null);
        c.EditorInitOrderContents(allowedContents: new[] { "A", "BA", "D", "DA", "DB", "F" });
        e.EditorInitOrderContents(limitedContents: "GI", maxLimitedContents: 1, highestContent: Symbol.I);
        h.EditorInitOrderContents(limitedContents: "JK", maxLimitedContents: 1);
        foreach (var bread in new[] { c, e, h }) EditorUtility.SetDirty(bread);
        foreach (var high in new[] { g, h, i, j, k, l }) high.EditorInitHighLevel();

        var types = new[] { root, a, b, c, d, e, f, g, h, i, j, k, l };
        PastryType TypeOf(Symbol s) => System.Array.Find(types, t => t.Symbol == s);

        // Condensed notation verbatim from design.md.
        var table = new (PastryType product, string notation)[]
        {
            (b, "A-A"),
            (c, "B-B"),
            (d, "A1-B-A-1"),
            (e, "D<A>D"),
            (f, "B-[C]-B"),
            (g, "F-B-F"),
            (h, "B>G<B"),
            (i, "F1>G<F-1"),
            (j, "D>G>[I]<G<D"),
            (k, "J1-J>F-F<J-J>F-F<1"),
            (l, "K>J>I(-G>F1(>D>B(-A)(-A))(>D>B(-A)(-A)))(-G>1)"),
        };

        var formulas = new List<FormulaAsset>();
        var made = new HashSet<Symbol> { a.Symbol };
        foreach (var (product, notation) in table)
        {
            var asset = Load<FormulaAsset>($"{DataDir}/Formula {product.Symbol}.asset");
            asset.EditorInit(product, notation, TypeOf);
            EditorUtility.SetDirty(asset);
            // design.md: a formula only uses elements that come before its product in the list.
            foreach (var ingredient in asset.Ingredients)
                if (!made.Contains(ingredient.type.Symbol))
                    Debug.LogWarning($"Bagels: {asset.name} ({notation}) uses {ingredient.type.Symbol}, which is not made earlier in the list.", asset);
            made.Add(product.Symbol);
            formulas.Add(asset);
        }

        // Stored in list order; the level checks them bottom-up, so the furthest-down match fires.
        var level = Load<LevelAsset>($"{DataDir}/Test Set.asset");
        level.EditorInit(root, l, a, formulas.ToArray());
        EditorUtility.SetDirty(level);
        return level;
    }

    static PastryType Pastry(string file, string display, Symbol symbol, Color color, LayoutKind layout,
                             int? price, bool unlockedAtStart = false, int orderLevels = 1)
    {
        var p = Load<PastryType>($"{DataDir}/Pastry {file}.asset");
        p.EditorInit(display, symbol, color, layout);
        p.EditorInitEconomy(price, unlockedAtStart, orderLevels);
        EditorUtility.SetDirty(p);
        return p;
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
        BakeUi(game);
        EditorUtility.SetDirty(game);
        game.RefreshPreview();

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
