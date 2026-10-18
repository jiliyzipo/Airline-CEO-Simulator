using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>确保符号字体导入时包含 Font Data，供 TMP 使用。</summary>
    public class SymbolFontImportSettings : AssetPostprocessor
    {
        private void OnPreprocessFont()
        {
            if (assetPath == null) return;
            if (!assetPath.EndsWith("SegoeUISymbol.ttf") && !assetPath.Contains("SegoeUISymbol"))
                return;

            var importer = (TrueTypeFontImporter)assetImporter;
            importer.fontTextureCase = FontTextureCase.Dynamic;
            importer.includeFontData = true;
            importer.fontRenderingMode = FontRenderingMode.Smooth;
        }
    }
}
