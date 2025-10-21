using LocoTool.Core.Abstractions;
using LocoTool.Core.Services;

namespace LocoTool.Tests;

public class TableIoTests
{
    [Fact]
    public void ResolveDelimiter_ParsesKnownValues()
    {
        var io = new TableIo();
        Assert.Equal('\t', io.ResolveDelimiter("\\t"));
        Assert.Equal('#', io.ResolveDelimiter("#"));
        Assert.Equal('|', io.ResolveDelimiter("|"));
        Assert.Equal('#', io.ResolveDelimiter(null));
    }

    [Fact]
    public void WriteRead_Roundtrip()
    {
        var io = new TableIo();
        var path = Path.GetTempFileName();
        try
        {
            var rows = new[] { new Row(1, 0, "hello", ""), new Row(2, 1, "world", "мир") };
            io.WriteRows(path, '\t', rows);
            var back = io.ReadRows(path, '\t').ToList();
            Assert.Equal(rows.Length, back.Count);
            Assert.Equal("hello", back[0].OrigText);
            Assert.Equal("мир", back[1].TranslatedText);
        }
        finally { File.Delete(path); }
    }
}

