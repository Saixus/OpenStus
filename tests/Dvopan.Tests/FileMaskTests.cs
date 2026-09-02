using Dvopan.Files;

namespace Dvopan.Tests;

public class FileMaskSingleMaskTests
{
    [Theory]
    [InlineData("readme.txt", "*.txt")]
    [InlineData("readme.txt", "*")]
    [InlineData("readme.txt", "readme.txt")]
    [InlineData("readme.txt", "readme.*")]
    [InlineData("readme.txt", "*.???")]
    [InlineData("readme.txt", "r*e.t?t")]
    [InlineData("a.b.c.txt", "*.txt")]
    [InlineData("noextension", "*")]
    public void MatchingMasks(string name, string mask) =>
        Assert.True(FileMask.IsMatch(name, mask, ignoreCase: true));

    [Theory]
    [InlineData("readme.txt", "*.cs")]
    [InlineData("readme.txt", "*.tx")]
    [InlineData("readme.txt", "readme")]
    [InlineData("readme.csv", "*.cs")]
    [InlineData("ab.txt", "a?c.txt")]
    [InlineData("readme.txt", "*.??")]
    public void NonMatchingMasks(string name, string mask) =>
        Assert.False(FileMask.IsMatch(name, mask, ignoreCase: true));

    [Fact]
    public void StarDotStarMeansEverythingIncludingNamesWithoutADot()
    {
        Assert.True(FileMask.IsMatch("noextension", "*.*", ignoreCase: true));
        Assert.True(FileMask.IsMatch("readme.txt", "*.*", ignoreCase: true));
        Assert.True(FileMask.IsMatch("", "*.*", ignoreCase: true));
    }

    [Fact]
    public void AnEmptyMaskMatchesNothing()
    {
        Assert.False(FileMask.IsMatch("a.txt", ""));
        Assert.False(FileMask.IsMatch("a.txt", "   "));
        Assert.False(FileMask.IsMatch("a.txt", null));
        Assert.False(FileMask.IsMatch(null, "*"));
    }

    [Fact]
    public void SurroundingWhitespaceIsIgnored() =>
        Assert.True(FileMask.IsMatch("a.txt", "  *.txt  ", ignoreCase: true));

    [Theory]
    [InlineData("apple.txt", "[abc]*.txt", true)]
    [InlineData("cherry.txt", "[abc]*.txt", true)]
    [InlineData("durian.txt", "[abc]*.txt", false)]
    [InlineData("m.cs", "[a-z].cs", true)]
    [InlineData("2.cs", "[a-z].cs", false)]
    [InlineData("2.cs", "[!a-z].cs", true)]
    [InlineData("m.cs", "[!a-z].cs", false)]
    [InlineData("2.cs", "[^a-z].cs", true)]
    public void CharacterClassesWork(string name, string mask, bool expected) =>
        Assert.Equal(expected, FileMask.IsMatch(name, mask, ignoreCase: false));

    [Fact]
    public void AnUnterminatedClassIsJustABracket()
    {
        Assert.True(FileMask.IsMatch("[abc", "[abc", ignoreCase: true));
        Assert.False(FileMask.IsMatch("a", "[abc", ignoreCase: true));
    }

    [Fact]
    public void AnEmptyClassIsTakenLiterally() =>
        Assert.True(FileMask.IsMatch("[]", "[]", ignoreCase: true));

    [Fact]
    public void RegexMetacharactersInAMaskAreLiteral()
    {
        Assert.True(FileMask.IsMatch("a+b.txt", "a+b.txt", ignoreCase: true));
        Assert.False(FileMask.IsMatch("ab.txt", "a+b.txt", ignoreCase: true));
        Assert.True(FileMask.IsMatch("v1.0(final).zip", "*(final).zip", ignoreCase: true));
        Assert.False(FileMask.IsMatch("axb.txt", "a.b.txt", ignoreCase: true));
    }

    [Fact]
    public void TheMaskIsAnchoredAtBothEnds()
    {
        Assert.StartsWith("^", FileMask.ToRegexPattern("*.txt"), StringComparison.Ordinal);
        Assert.EndsWith("$", FileMask.ToRegexPattern("*.txt"), StringComparison.Ordinal);
        Assert.False(FileMask.IsMatch("xxreadme.txtxx", "readme.txt", ignoreCase: true));
    }

    [Fact]
    public void CaseSensitivityIsSelectableAndDefaultsToThePlatform()
    {
        Assert.True(FileMask.IsMatch("README.TXT", "*.txt", ignoreCase: true));
        Assert.False(FileMask.IsMatch("README.TXT", "*.txt", ignoreCase: false));

        Assert.Equal(OperatingSystem.IsWindows(), FileMask.DefaultIgnoreCase);
        Assert.Equal(FileMask.DefaultIgnoreCase, FileMask.IsMatch("README.TXT", "*.txt"));
    }

    [Fact]
    public void ARepeatedMaskGivesTheSameAnswerAfterTheCacheIsDropped()
    {
        Assert.True(FileMask.IsMatch("a.txt", "*.txt", ignoreCase: true));
        Assert.True(FileMask.IsMatch("b.txt", "*.txt", ignoreCase: true));

        FileMask.ClearCache();

        Assert.True(FileMask.IsMatch("c.txt", "*.txt", ignoreCase: true));
        Assert.False(FileMask.IsMatch("c.cs", "*.txt", ignoreCase: true));
    }

    [Fact]
    public void ManyDistinctMasksDoNotBreakTheCache()
    {
        for (int i = 0; i < 700; i++)
        {
            Assert.True(FileMask.IsMatch($"file{i}.txt", $"file{i}.*", ignoreCase: true));
        }

        Assert.True(FileMask.IsMatch("a.txt", "*.txt", ignoreCase: true));
    }
}

public class FileMaskListTests
{
    [Fact]
    public void EitherSeparatorSplitsAList()
    {
        Assert.True(FileMask.IsMatchAny("a.cs", "*.cs,*.vb", ignoreCase: true));
        Assert.True(FileMask.IsMatchAny("a.vb", "*.cs;*.vb", ignoreCase: true));
        Assert.True(FileMask.IsMatchAny("a.vb", "*.cs, *.vb", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("a.txt", "*.cs,*.vb", ignoreCase: true));
    }

    [Fact]
    public void AnExclusionOverridesTheIncludes()
    {
        Assert.True(FileMask.IsMatchAny("Program.cs", "*.cs,!Generated*", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("Generated.cs", "*.cs,!Generated*", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("GeneratedModel.cs", "*.cs,!Generated*", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("Program.vb", "*.cs,!Generated*", ignoreCase: true));
    }

    [Fact]
    public void TheExclusionCountsWhereverItAppearsInTheList()
    {
        Assert.False(FileMask.IsMatchAny("Generated.cs", "!Generated*,*.cs", ignoreCase: true));
        Assert.True(FileMask.IsMatchAny("Program.cs", "!Generated*,*.cs", ignoreCase: true));
    }

    [Fact]
    public void AListOfNothingButExclusionsLetsEverythingElseThrough()
    {
        Assert.True(FileMask.IsMatchAny("a.txt", "!*.tmp", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("a.tmp", "!*.tmp", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("a.tmp", "!*.tmp,!*.bak", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("a.bak", "!*.tmp,!*.bak", ignoreCase: true));
        Assert.True(FileMask.IsMatchAny("a.cs", "!*.tmp,!*.bak", ignoreCase: true));
    }

    [Fact]
    public void FarStyleExcludesFollowAPipe()
    {
        Assert.True(FileMask.IsMatchAny("Program.cs", "*.cs|*.g.cs", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("Model.g.cs", "*.cs|*.g.cs", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("Program.vb", "*.cs|*.g.cs", ignoreCase: true));

        Assert.True(FileMask.IsMatchAny("vector.hpp", "*.cpp,*.hpp|*_test*", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("vector_test.cpp", "*.cpp,*.hpp|*_test*", ignoreCase: true));
    }

    /// <summary>A '|' inside a character class is one of the class's members, not the separator.</summary>
    [Fact]
    public void APipeInsideACharacterClassDoesNotSplitTheList()
    {
        Assert.True(FileMask.IsMatchAny("apple.cs", "[a|b]*.cs", ignoreCase: true));
        Assert.True(FileMask.IsMatchAny("berry.cs", "[a|b]*.cs", ignoreCase: true));
        Assert.True(FileMask.IsMatchAny("|weird.cs", "[a|b]*.cs", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("cherry.cs", "[a|b]*.cs", ignoreCase: true));

        // A real separator after the class still splits.
        Assert.True(FileMask.IsMatchAny("apple.cs", "[a|b]*.cs|*.g.cs", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("apple.g.cs", "[a|b]*.cs|*.g.cs", ignoreCase: true));
    }

    [Fact]
    public void AnEmptyIncludeHalfBeforeThePipeMeansEverything()
    {
        Assert.True(FileMask.IsMatchAny("a.txt", "|*.bak", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("a.bak", "|*.bak", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("a.tmp", "|*.bak;*.tmp", ignoreCase: true));
        Assert.True(FileMask.IsMatchAny("a.txt", "|", ignoreCase: true));
    }

    [Fact]
    public void AnEmptyExcludeHalfAfterThePipeChangesNothing()
    {
        Assert.True(FileMask.IsMatchAny("a.cs", "*.cs|", ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("a.vb", "*.cs|", ignoreCase: true));
    }

    [Fact]
    public void TheTwoExcludeSpellingsMixFreely()
    {
        const string list = "*.cs,!Generated*|*.g.cs";

        Assert.True(FileMask.IsMatchAny("Program.cs", list, ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("Generated.cs", list, ignoreCase: true));
        Assert.False(FileMask.IsMatchAny("Model.g.cs", list, ignoreCase: true));

        // A '!' prefix inside the exclude half is redundant but harmless.
        Assert.False(FileMask.IsMatchAny("a.bak", "*|!*.bak", ignoreCase: true));
    }

    [Fact]
    public void AnEmptyListFiltersNothingOut()
    {
        Assert.True(FileMask.IsMatchAny("a.txt", ""));
        Assert.True(FileMask.IsMatchAny("a.txt", null));
        Assert.True(FileMask.IsMatchAny("a.txt", " , ; "));
        Assert.False(FileMask.IsMatchAny(null, "*"));
    }

    [Fact]
    public void ABareExclamationMarkIsIgnored() =>
        Assert.True(FileMask.IsMatchAny("a.txt", "*.txt,!", ignoreCase: true));

    [Fact]
    public void SplitListTrimsAndDropsEmptyItems()
    {
        Assert.Equal(["*.cs", "!Generated*"], FileMask.SplitList(" *.cs , !Generated* "));
        Assert.Equal(["a", "b", "c"], FileMask.SplitList("a;b,c"));
        Assert.Empty(FileMask.SplitList(""));
        Assert.Empty(FileMask.SplitList(null));
        Assert.Empty(FileMask.SplitList("  ,  ;  "));
    }

    [Fact]
    public void TheEveryFileMaskMatchesEveryName()
    {
        string[] names = ["a", "a.txt", ".gitignore", "UPPER.CS", "with space.bin"];

        Assert.All(names, n => Assert.True(FileMask.IsMatchAny(n, "*")));
        Assert.All(names, n => Assert.True(FileMask.IsMatchAny(n, "*.*")));
    }
}
