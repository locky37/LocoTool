using System.Reflection;

namespace LocoTool.Tests;

public class ApplyCommandHelpersTests
{
    [Fact]
    public void ReadTablePossiblyDirectory_MergesAllTsvRows()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var header = "original_line_no\tfield_index\trecord_id_guess\torig_text\ttranslated_text";
            File.WriteAllText(Path.Combine(dir.FullName, "a.tsv"), header + "\n1\t0\tA\t一\t");
            File.WriteAllText(Path.Combine(dir.FullName, "b.tsv"), header + "\n2\t0\tB\t二\t");

            var cmdType = typeof(LocoTool.Cli.ApplyCommand);
            var mi = cmdType.GetMethod("ReadTablePossiblyDirectory", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(mi);
            var merged = (string) mi!.Invoke(null, new object?[] { dir.FullName })!;
            var lines = merged.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal(3, lines.Length); // header + 2 rows
            Assert.Equal(header, lines[0]);
        }
        finally
        {
            Directory.Delete(dir.FullName, true);
        }
    }
}

