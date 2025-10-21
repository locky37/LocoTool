using LocoTool.Core.Services;

namespace LocoTool.Tests;

public class TranslationMemoryTests
{
    [Fact]
    public void JsonTranslationMemory_PersistRoundtrip()
    {
        var path = Path.GetTempFileName();
        try
        {
            var tm = new JsonTranslationMemory(path);
            tm.Add("一", "one");
            tm.Save();
            var tm2 = new JsonTranslationMemory(path);
            Assert.True(tm2.TryGet("一", out var v));
            Assert.Equal("one", v);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BatchCache_PersistAndLookup()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "batchcache.json");
            var cache = new BatchCache(path);
            var key = BatchCache.ComputeKey(new[] { "a", "b" }, "en", "zh");
            cache.Put(key, new[] { "A", "B" });
            cache.Save();
            var cache2 = new BatchCache(path);
            Assert.True(cache2.TryGet(key, out var outVals));
            Assert.Equal("A", outVals[0]);
            Assert.Equal("B", outVals[1]);
        }
        finally { Directory.Delete(dir.FullName, true); }
    }
}

