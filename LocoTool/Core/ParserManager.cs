using System.Reflection;
using LocoTool.Abstractions;

namespace LocoTool.Core;

public sealed class ParserManager
{
    private readonly List<ILocParser> _parsers = new();

    public IReadOnlyList<ILocParser> Parsers => _parsers;

    public void LoadFromFolder(string folder, IEnumerable<string>? onlyAssemblies = null)
    {
        if (!Directory.Exists(folder)) return;
        var files = Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly);

        if (onlyAssemblies is not null)
        {
            var only = new HashSet<string>(onlyAssemblies.Select(x => x.ToLowerInvariant()));
            files = files.Where(f => only.Contains(Path.GetFileName(f).ToLowerInvariant()));
        }

        foreach (var path in files)
        {
            try
            {
                var asm = Assembly.LoadFrom(path);
                var impls = asm.GetTypes()
                    .Where(t => !t.IsAbstract && typeof(ILocParser).IsAssignableFrom(t));

                foreach (var t in impls)
                {
                    if (Activator.CreateInstance(t) is ILocParser p)
                        _parsers.Add(p);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[parsers] skip {path}: {ex.Message}");
            }
        }
    }

    // Выбор по имени (--parser hash) или автодетекту
    public ILocParser? Resolve(string? name, string? filePath, string? contentSample = null)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return _parsers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(filePath))
            return _parsers.FirstOrDefault(p => p.CanHandle(filePath, contentSample));

        return _parsers.FirstOrDefault(); // fallback
    }
}

