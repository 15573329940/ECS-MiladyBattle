using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var sourceDir = args.Length > 0 ? args[0] : Path.Combine(repoRoot, "Assets", "Scripts");
        var outDir = args.Length > 1 ? args[1] : Path.Combine(sourceDir, "docs");
        var indexFile = args.Length > 2 ? args[2] : Path.Combine(sourceDir, "DOCUMENTATION.md");

        Console.WriteLine($"DocGen: scanning '{sourceDir}' -> '{outDir}'");
        if (!Directory.Exists(sourceDir))
        {
            Console.Error.WriteLine("Source directory not found.");
            return 2;
        }

        Directory.CreateDirectory(outDir);
        var csFiles = Directory.GetFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var summarySb = new StringBuilder();
        summarySb.AppendLine("# Assets/Scripts Documentation");
        summarySb.AppendLine();
        summarySb.AppendLine($"Generated: {DateTime.UtcNow:O}");
        summarySb.AppendLine();

        int generated = 0;
        var errors = new List<string>();

        foreach (var file in csFiles)
        {
            try
            {
                var relPath = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
                var mdPath = Path.Combine(outDir, relPath + ".md");
                Directory.CreateDirectory(Path.GetDirectoryName(mdPath));

                var code = await File.ReadAllTextAsync(file);
                var tree = CSharpSyntaxTree.ParseText(code);
                var root = await tree.GetRootAsync();
                var sb = new StringBuilder();

                sb.AppendLine($"---");
                sb.AppendLine($"source: {relPath}");
                sb.AppendLine($"generated: {DateTime.UtcNow:O}");
                sb.AppendLine($"---");
                sb.AppendLine();
                sb.AppendLine($"# {Path.GetFileName(file)}");
                sb.AppendLine();

                // Try to get file-level leading trivia summary comment
                var firstTrivia = root.GetLeadingTrivia().ToFullString().Trim();
                if (!string.IsNullOrWhiteSpace(firstTrivia))
                {
                    sb.AppendLine("## File header");
                    sb.AppendLine();
                    sb.AppendLine($"```");
                    foreach (var line in firstTrivia.Split(new[] {"\r\n","\n"}, StringSplitOptions.None))
                        sb.AppendLine(line);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }

                // Extract all type declarations in file
                var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToArray();
                if (types.Length == 0)
                {
                    sb.AppendLine("_No type declarations found in this file._");
                }
                else
                {
                    sb.AppendLine("## Types");
                    sb.AppendLine();
                    foreach (var t in types)
                    {
                        var kind = t.Kind().ToString();
                        var name = t.Identifier.Text;
                        var ns = t.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "<global>";
                        sb.AppendLine($"### {name} ({ns})");
                        sb.AppendLine();

                        // summary from XML doc comments (if any)
                        var leading = t.GetLeadingTrivia().ToFullString();
                        var xmlSummary = ExtractXmlSummary(leading);
                        if (!string.IsNullOrWhiteSpace(xmlSummary))
                        {
                            sb.AppendLine(xmlSummary);
                            sb.AppendLine();
                        }
                        else
                        {
                            sb.AppendLine($"_Auto-generated summary: {GenerateAutoSummary(name, t)}_");
                            sb.AppendLine();
                        }

                        // inheritance
                        var baseList = t.BaseList?.Types.Select(bt => bt.ToString()).ToArray() ?? Array.Empty<string>();
                        if (baseList.Length > 0)
                        {
                            sb.AppendLine($"**Inherits/Implements:** {string.Join(", ", baseList)}");
                            sb.AppendLine();
                        }

                        // public members
                        sb.AppendLine("**Public members (sample)**:");
                        sb.AppendLine();
                        var members = t.Members.Where(m =>
                            (m is MethodDeclarationSyntax md && md.Modifiers.Any(SyntaxKind.PublicKeyword)) ||
                            (m is PropertyDeclarationSyntax pd && pd.Modifiers.Any(SyntaxKind.PublicKeyword)) ||
                            (m is FieldDeclarationSyntax fd && fd.Modifiers.Any(SyntaxKind.PublicKeyword))
                        ).ToArray();

                        if (members.Length == 0)
                        {
                            sb.AppendLine("_No public members found._");
                        }
                        else
                        {
                            sb.AppendLine("```csharp");
                            foreach (var m in members.Take(20))
                            {
                                var line = m.ToFullString().Trim();
                                var firstLine = line.Split(new[] {"\r\n","\n"}, StringSplitOptions.None).First().Trim();
                                sb.AppendLine(firstLine);
                            }
                            sb.AppendLine("```");
                        }

                        // TODOs
                        var todos = t.DescendantTrivia().Where(tr => tr.ToString().Contains("TODO")).Select(tr => tr.ToString().Trim()).Distinct().ToArray();
                        if (todos.Length > 0)
                        {
                            sb.AppendLine();
                            sb.AppendLine("**TODOs found in type:**");
                            sb.AppendLine();
                            foreach (var td in todos) sb.AppendLine("- " + td.Replace("\r\n"," ").Replace("\n"," "));
                        }

                        sb.AppendLine();
                    }
                }

                // Write file
                await File.WriteAllTextAsync(mdPath, sb.ToString());
                summarySb.AppendLine($"- [{relPath}](docs/{relPath}.md)");
                generated++;
            }
            catch (Exception ex)
            {
                errors.Add($"{file}: {ex.Message}");
            }
        }

        // Write index
        summarySb.AppendLine();
        summarySb.AppendLine($"Generated files: {generated}");
        if (errors.Count > 0)
        {
            summarySb.AppendLine();
            summarySb.AppendLine("## Errors");
            foreach (var e in errors) summarySb.AppendLine("- " + e);
        }

        await File.WriteAllTextAsync(indexFile, summarySb.ToString());

        Console.WriteLine($"Done. Generated {generated} files. Errors: {errors.Count}");
        return 0;
    }

    static string ExtractXmlSummary(string leadingTrivia)
    {
        if (string.IsNullOrWhiteSpace(leadingTrivia)) return null;
        // look for /// <summary> ... </summary>
        var lines = leadingTrivia.Split(new[] {"\r\n","\n"}, StringSplitOptions.None);
        var sb = new StringBuilder();
        bool inSummary = false;
        foreach (var l in lines)
        {
            var t = l.Trim();
            if (t.StartsWith("/// <summary>")) { inSummary = true; sb.AppendLine(t.Replace("/// <summary>", "").Trim()); continue; }
            if (inSummary)
            {
                if (t.StartsWith("/// </summary>")) { break; }
                sb.AppendLine(t.TrimStart('/',' ').Trim());
            }
        }
        var res = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(res) ? null : res;
    }

    static string GenerateAutoSummary(string name, TypeDeclarationSyntax t)
    {
        // split PascalCase
        var words = System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
        var kind = t.Keyword.Text; // class/struct/interface
        return $"{words} — auto-generated {kind} summary.";
    }
}

