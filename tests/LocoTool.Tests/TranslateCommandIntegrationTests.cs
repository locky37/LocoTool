using LocoTool.Cli;
using LocoTool.Config;
using LocoTool.Core.Abstractions;
using LocoTool.Core.Services;

namespace LocoTool.Tests;

public class TranslateCommandIntegrationTests
{
    private sealed class FakeConfigService : IConfigService
    {
        public AppConfigResult Load(string? path)
        {
            var cfg = new AppConfig
            {
                Yandex = new YandexConfig { ApiKey = "x", DefaultSourceLang = "zh", DefaultTargetLang = "en" },
                Limits = new LimitConfig { MaxCharsPerRequest = 10000 },
                Files = new FileDefaults(),
                Parsers = new ParsersConfig { Folder = "parsers" }
            };
            return AppConfigResult.Ok(cfg);
        }
    }

    private sealed class FakeGlossaryService : IGlossaryService
    {
        public (string src, string dst, bool exact)[] Load(string? path) => Array.Empty<(string, string, bool)>();
        public (string src, string dst, bool exact)[] EnforceLimit((string src, string dst, bool exact)[] pairs, int maxPairs) => pairs;
    }

    private sealed class EchoTranslateClient : ITranslateClient
    {
        public Task<IReadOnlyList<string>> TranslateBatchAsync(IEnumerable<string> texts, string target, string? source, IEnumerable<(string src, string dst, bool exact)>? glossary, bool speller, CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<string>)texts.Select(t => $"T:{t}").ToArray());
    }

    [Fact]
    public async Task Translate_DirectoryToDirectory_ProducesTranslatedFiles()
    {
        var inDir = Directory.CreateTempSubdirectory();
        var outDir = Directory.CreateTempSubdirectory();
        try
        {
            var header = "original_line_no\tfield_index\trecord_id_guess\torig_text\ttranslated_text";
            var content = string.Join('\n', new []
            {
                header,
                "1\t0\tA\t一\t",
                "2\t0\tA\t二\t"
            });
            var inFile = Path.Combine(inDir.FullName, "partA.tsv");
            File.WriteAllText(inFile, content);

            var cmd = new TranslateCommand(new FakeConfigService(), new FakeGlossaryService(), new EchoTranslateClient(), new TableIo(), new StatsService(new TableIo()));
            var args = new[] { "translate", inDir.FullName, outDir.FullName, "--delimiter", "\\t" };
            var ctx = CommandContext.FromArgs(args);
            var rc = await cmd.RunAsync(ctx, CancellationToken.None);
            Assert.Equal(0, rc);

            var outFile = Path.Combine(outDir.FullName, "partA.tsv");
            Assert.True(File.Exists(outFile));
            var lines = File.ReadAllLines(outFile);
            Assert.True(lines.Length >= 3);
            var row1 = lines[1].Split('\t');
            var row2 = lines[2].Split('\t');
            int iOrig = Array.IndexOf(lines[0].Split('\t'), "orig_text");
            int iTr = Array.IndexOf(lines[0].Split('\t'), "translated_text");
            Assert.Equal($"T:{row1[iOrig]}", row1[iTr]);
            Assert.Equal($"T:{row2[iOrig]}", row2[iTr]);
        }
        finally
        {
            try { Directory.Delete(inDir.FullName, true); } catch { }
            try { Directory.Delete(outDir.FullName, true); } catch { }
        }
    }
}

