using LocoTool.Core.Services;

namespace LocoTool.Tests;

public class GlobalTmTests
{
    [Fact]
    public void JsonlGtm_AppendAndLookup()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var gtm = new JsonlGlobalTranslationMemory(root.FullName, "default", true);
            gtm.Append("打开地图", "Open Map", "zh", "en", 1.0, true);
            Assert.True(gtm.TryGet("打开地图", "zh", "en", null, out var dst));
            Assert.Equal("Open Map", dst);
        }
        finally { Directory.Delete(root.FullName, true); }
    }

    [Fact]
    public void JsonlGtm_ExportImport_Tsv()
    {
        var root = Directory.CreateTempSubdirectory();
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var gtm = new JsonlGlobalTranslationMemory(root.FullName, "team", true);
            gtm.Append("关闭地图", "Close Map", "zh", "en", 1.0, true);
            var tsv = Path.Combine(temp.FullName, "gtm.tsv");
            gtm.Export(tsv);
            var gtm2 = new JsonlGlobalTranslationMemory(root.FullName, "team2", true);
            gtm2.Import(tsv);
            Assert.True(gtm2.TryGet("关闭地图", "zh", "en", null, out var dst));
            Assert.Equal("Close Map", dst);
        }
        finally { Directory.Delete(root.FullName, true); Directory.Delete(temp.FullName, true); }
    }
}

