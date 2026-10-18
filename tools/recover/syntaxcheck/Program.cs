using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length == 0)
{
    Console.WriteLine("usage: syntaxcheck <file.cs> [more.cs ...]");
    return 1;
}

int totalErrors = 0, badFiles = 0;
foreach (string path in args)
{
    string text = File.ReadAllText(path, System.Text.Encoding.UTF8);
    var tree = CSharpSyntaxTree.ParseText(SourceText.From(text, System.Text.Encoding.UTF8),
        new CSharpParseOptions(LanguageVersion.Latest));
    var diags = tree.GetDiagnostics()
        .Where(d => d.Severity == DiagnosticSeverity.Error)
        .OrderBy(d => d.Location.SourceSpan.Start)
        .ToList();
    if (diags.Count == 0) continue;

    badFiles++;
    totalErrors += diags.Count;
    var lines = text.Split('\n');
    Console.WriteLine($"file={path} syntaxErrors={diags.Count}");
    foreach (var d in diags.Take(30))
    {
        var span = d.Location.GetLineSpan();
        int ln = span.StartLinePosition.Line + 1;
        string src = ln - 1 < lines.Length ? lines[ln - 1].Trim() : "";
        if (src.Length > 150) src = src.Substring(0, 150);
        Console.WriteLine($"  L{ln}c{span.StartLinePosition.Character + 1}: {d.Id} {d.GetMessage()}  ||  {src}");
    }
}

Console.WriteLine($"checkedFiles={args.Length} filesWithErrors={badFiles} totalSyntaxErrors={totalErrors}");
return 0;
