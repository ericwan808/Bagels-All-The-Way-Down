using System;
using System.Linq;
using Bagels.Core;
using Bagels.Data;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shows a formula's pattern in the design.md text notation and lets developers set it by typing either form,
/// e.g. <c>F1&gt;G&lt;F-1</c> or <c>F G F | 0&gt;1 1&lt;2 0-2</c>.
/// </summary>
[CustomEditor(typeof(FormulaAsset))]
public sealed class FormulaAssetEditor : Editor
{
    string input;
    string error;

    public override void OnInspectorGUI()
    {
        var formula = (FormulaAsset)target;

        EditorGUILayout.LabelField("Notation", EditorStyles.boldLabel);
        EditorGUILayout.SelectableLabel(formula.Notation, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

        input ??= formula.Notation;
        input = EditorGUILayout.TextField("Set from text", input);
        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(input)))
            if (GUILayout.Button("Apply notation")) Apply(formula);
        if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);

        EditorGUILayout.Space();
        DrawDefaultInspector();
    }

    void Apply(FormulaAsset formula)
    {
        try
        {
            // Prefer pastry types in the formula's own folder, so symbols resolve within one level's set.
            var folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(formula));
            var types = AssetDatabase.FindAssets("t:PastryType")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => System.IO.Path.GetDirectoryName(p) == folder ? 0 : 1)
                .Select(AssetDatabase.LoadAssetAtPath<PastryType>)
                .ToArray();
            FormulaNotation.ToFormula(formula.name, default, input); // validate before touching the asset
            Undo.RecordObject(formula, "Apply formula notation");
            formula.EditorInit(formula.Product, input, s => Array.Find(types, t => t != null && t.Symbol == s));
            EditorUtility.SetDirty(formula);
            input = formula.Notation;
            error = null;
        }
        catch (Exception e) when (e is FormatException || e is ArgumentException || e is InvalidOperationException)
        {
            error = e.Message;
        }
    }
}
