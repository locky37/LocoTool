using System.Text;
using LocoTool.Core.Abstractions;

namespace LocoTool.Core.Services;

public sealed class HumanLoopService : IHumanLoop
{
    public void ExportReview(string path, IEnumerable<(string orig, string mtSuggest)> items)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var sw = new StreamWriter(path, false, Encoding.UTF8);
        sw.WriteLine("orig_text\tmt_suggest\tfinal_text");
        foreach (var (orig, mt) in items)
        {
            sw.WriteLine(string.Join('\t', new[]
            {
                (orig ?? string.Empty).Replace("\n"," "),
                (mt ?? string.Empty).Replace("\n"," "),
                string.Empty
            }));
        }
    }
}

