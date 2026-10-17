using DeskKeeper.Domain.Rules;

namespace DeskKeeper.Tests.DomainTests;

public class GlobTests
{
    [Theory]
    [InlineData("三班课表.xlsx", "*课表*", true)]
    [InlineData("课程安排.xlsx", "*课表*", false)]
    [InlineData("a.tmp", "*.tmp", true)]
    [InlineData("a.tmpx", "*.tmp", false)]
    [InlineData("file1.txt", "file?.txt", true)]
    [InlineData("file10.txt", "file?.txt", false)]
    [InlineData("A.TMP", "*.tmp", true)]
    public void Glob_matches_expected(string input, string pattern, bool expected)
    {
        Assert.Equal(expected, Glob.IsMatch(input, pattern));
    }

    [Fact]
    public void MatchesAnyExtension_hits_any()
    {
        Assert.True(Glob.MatchesAnyExtension("下载.crdownload", new[] { "*.tmp", "*.crdownload" }));
        Assert.False(Glob.MatchesAnyExtension("下载.docx", new[] { "*.tmp", "*.crdownload" }));
    }
}

