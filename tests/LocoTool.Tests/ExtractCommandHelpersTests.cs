using System.Reflection;

namespace LocoTool.Tests;

public class ExtractCommandHelpersTests
{
    [Fact]
    public void GroupByRecordId_SplitsRowsByTag()
    {
        var tsv = string.Join("\n", new []
        {
            "original_line_no\tfield_index\trecord_id_guess\torig_text\ttranslated_text",
            "1\t0\tA\t一\t",
            "2\t0\tA\t二\t",
            "3\t0\tB\t三\t"
        });

        var cmdType = typeof(LocoTool.Cli.ExtractCommand);
        var mi = cmdType.GetMethod("GroupByRecordId", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(mi);
        var groups = (List<(string tag, string content)>) mi!.Invoke(null, new object?[] { tsv, '\t' })!;
        var dict = groups.ToDictionary(g => g.tag, g => g.content);
        Assert.True(dict.ContainsKey("A"));
        Assert.True(dict.ContainsKey("B"));
        Assert.Equal(3, dict["A"].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length); // header + 2 rows
        Assert.Equal(2, dict["B"].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length); // header + 1 row
    }
}

