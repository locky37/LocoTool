using HashDelimited.Parser;

namespace LocoTool.Tests;

public class HashPlusParserTests
{
    [Fact]
    public void Extract_SetsRecordIdGuessToSectionTag()
    {
        var parser = new HashPlusParser();
        var input = string.Join(Environment.NewLine, new[]
        {
            "keywordfilter|48645",
            "1#曾#1#0#",
            "2#安#1#0#",
            "",
            "randomname|2255",
            "3#柏#1#0#"
        });

        var tsv = parser.Extract(input, new LocoTool.Abstractions.ParserOptions { TableDelimiter = '\t' });
        var lines = tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.True(lines.Length >= 2); // header + rows

        var header = lines[0].Split('\t');
        int iTag = Array.IndexOf(header, "record_id_guess");
        int iOrig = Array.IndexOf(header, "orig_text");
        Assert.True(iTag >= 0 && iOrig >= 0);

        // first two rows should have tag keywordfilter
        var row1 = lines[1].Split('\t');
        var row2 = lines[2].Split('\t');
        Assert.Equal("keywordfilter", row1[iTag]);
        Assert.Equal("keywordfilter", row2[iTag]);

        // last row should have tag randomname
        var rowLast = lines[^1].Split('\t');
        Assert.Equal("randomname", rowLast[iTag]);
    }
}

