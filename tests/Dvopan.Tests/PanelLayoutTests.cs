using Dvopan.Panels;
using Dvopan.Rendering;

namespace Dvopan.Tests;

public class PanelViewModeTests
{
    [Fact]
    public void AcceleratorNumbersRoundTrip()
    {
        for (int n = PanelViewModes.MinNumber; n <= PanelViewModes.MaxNumber; n++)
        {
            Assert.Equal(n, PanelViewModes.ToNumber(PanelViewModes.FromNumber(n)));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(-3)]
    public void OutOfRangeNumbersFallBackToMedium(int number) =>
        Assert.Equal(PanelViewMode.Medium, PanelViewModes.FromNumber(number));

    [Fact]
    public void DefaultIsMedium() => Assert.Equal(PanelViewMode.Medium, PanelViewModes.Default);

    [Theory]
    [InlineData(PanelViewMode.Descriptions)]
    [InlineData(PanelViewMode.LongDescriptions)]
    [InlineData(PanelViewMode.FileOwners)]
    [InlineData(PanelViewMode.Links)]
    public void ModesWithoutDataFallBackToFull(PanelViewMode mode) =>
        Assert.Equal(PanelViewMode.Full, PanelViewModes.Effective(mode));

    [Theory]
    [InlineData(PanelViewMode.Brief)]
    [InlineData(PanelViewMode.Medium)]
    [InlineData(PanelViewMode.Full)]
    [InlineData(PanelViewMode.Wide)]
    [InlineData(PanelViewMode.Detailed)]
    public void RealModesAreTheirOwnEffectiveMode(PanelViewMode mode) =>
        Assert.Equal(mode, PanelViewModes.Effective(mode));

    [Fact]
    public void DisplayNameDropsTheHotkeyMarker()
    {
        Assert.Equal("Detailed", PanelViewModes.DisplayName(PanelViewMode.Detailed));
        Assert.Equal("Long descriptions", PanelViewModes.DisplayName(PanelViewMode.LongDescriptions));
    }
}

public class PanelColumnLayoutStripeTests
{
    [Theory]
    [InlineData(PanelViewMode.Brief, 3)]
    [InlineData(PanelViewMode.Medium, 2)]
    [InlineData(PanelViewMode.Wide, 1)]
    [InlineData(PanelViewMode.Full, 1)]
    [InlineData(PanelViewMode.Detailed, 1)]
    [InlineData(PanelViewMode.FileOwners, 1)]
    public void StripeCountsFollowTheViewMode(PanelViewMode mode, int expected) =>
        Assert.Equal(expected, PanelColumnLayout.StripesOf(mode));

    [Fact]
    public void MediumSplitsTheInnerWidthInTwoWithTheRemainderOnTheLeft()
    {
        // 38 inner cells: one separator, then 37 shared out as 19 + 18.
        var layout = PanelColumnLayout.Compute(PanelViewMode.Medium, 38);

        Assert.Equal(2, layout.Stripes);
        Assert.Equal(19, layout.StripeWidth(0));
        Assert.Equal(18, layout.StripeWidth(1));
        Assert.Equal(0, layout.StripeStart(0));
        Assert.Equal(20, layout.StripeStart(1));
        Assert.Equal(new[] { 19 }, layout.Separators);
    }

    [Fact]
    public void BriefSplitsTheInnerWidthInThree()
    {
        // 38 inner cells: two separators, then 36 shared out as 12 + 12 + 12.
        var layout = PanelColumnLayout.Compute(PanelViewMode.Brief, 38);

        Assert.Equal(3, layout.Stripes);
        Assert.Equal(
            new[] { 12, 12, 12 },
            new[] { layout.StripeWidth(0), layout.StripeWidth(1), layout.StripeWidth(2) });
        Assert.Equal(
            new[] { 0, 13, 26 },
            new[] { layout.StripeStart(0), layout.StripeStart(1), layout.StripeStart(2) });
        Assert.Equal(new[] { 12, 25 }, layout.Separators);
    }

    [Fact]
    public void TheRemainderGoesToTheLeftmostStripes()
    {
        // 40 inner cells, three stripes: 38 shared out as 13 + 13 + 12.
        var layout = PanelColumnLayout.Compute(PanelViewMode.Brief, 40);

        Assert.Equal(13, layout.StripeWidth(0));
        Assert.Equal(13, layout.StripeWidth(1));
        Assert.Equal(12, layout.StripeWidth(2));
    }

    [Theory]
    [InlineData(PanelViewMode.Brief, 3)]
    [InlineData(PanelViewMode.Medium, 2)]
    public void StripesCollapseWhenThePanelCannotHoldThem(PanelViewMode mode, int normalStripes)
    {
        Assert.Equal(normalStripes, PanelColumnLayout.Compute(mode, 60).Stripes);
        Assert.Equal(1, PanelColumnLayout.Compute(mode, 1).Stripes);
    }

    [Fact]
    public void AZeroWidthPanelHasNoColumnsAtAll()
    {
        var layout = PanelColumnLayout.Compute(PanelViewMode.Full, 0);

        Assert.True(layout.IsEmpty);
        Assert.Empty(layout.Columns);
        Assert.Empty(layout.Separators);
        Assert.Equal(0, layout.Stripes);
        Assert.Equal(-1, layout.StripeAt(0));
    }
}

public class PanelColumnLayoutFieldTests
{
    [Fact]
    public void FullHasNameSizeDateAndTime()
    {
        var layout = PanelColumnLayout.Compute(PanelViewMode.Full, 38);

        Assert.Equal(1, layout.Stripes);
        Assert.Equal(4, layout.FieldsPerStripe);
        Assert.Equal(
            new[] { PanelColumnKind.Name, PanelColumnKind.Size, PanelColumnKind.Date, PanelColumnKind.Time },
            layout.Fields);

        // 38 - (9 + 8 + 5) - 3 separators = 13 cells of name.
        Assert.Equal(new PanelColumn(PanelColumnKind.Name, 0, 0, 13), layout.Column(0, 0));
        Assert.Equal(new PanelColumn(PanelColumnKind.Size, 0, 14, 9), layout.Column(0, 1));
        Assert.Equal(new PanelColumn(PanelColumnKind.Date, 0, 24, 8), layout.Column(0, 2));
        Assert.Equal(new PanelColumn(PanelColumnKind.Time, 0, 33, 5), layout.Column(0, 3));
        Assert.Equal(new[] { 13, 23, 32 }, layout.Separators);
    }

    [Fact]
    public void DetailedAddsTheAttributeColumn()
    {
        var layout = PanelColumnLayout.Compute(PanelViewMode.Detailed, 60);

        Assert.Equal(5, layout.FieldsPerStripe);
        Assert.Equal(PanelColumnKind.Attributes, layout.Column(0, 4).Kind);
        Assert.Equal(PanelColumn.AttributesWidth, layout.Column(0, 4).Width);

        // 60 - (9 + 8 + 5 + 5) - 4 separators = 29 cells of name.
        Assert.Equal(29, layout.Column(0, 0).Width);
        Assert.Equal(60, layout.Column(0, 4).Right);
    }

    [Fact]
    public void WideIsASingleStripeOfAWideNameAndSize()
    {
        // Mode 4 is one stripe of N,S: the mode is called Wide for the name column's sake,
        // not because the pair repeats.
        var layout = PanelColumnLayout.Compute(PanelViewMode.Wide, 38);

        Assert.Equal(1, layout.Stripes);
        Assert.Equal(2, layout.FieldsPerStripe);
        Assert.Equal(new[] { PanelColumnKind.Name, PanelColumnKind.Size }, layout.Fields);

        // 38 - (9 + 1) = 28 cells of name.
        Assert.Equal(28, layout.Column(0, 0).Width);
        Assert.Equal(PanelColumn.SizeWidth, layout.Column(0, 1).Width);
        Assert.Equal(38, layout.Column(0, 1).Right);
    }

    [Theory]
    [InlineData(PanelViewMode.Brief)]
    [InlineData(PanelViewMode.Medium)]
    public void NameOnlyModesHaveOneFieldPerStripe(PanelViewMode mode)
    {
        var layout = PanelColumnLayout.Compute(mode, 50);

        Assert.Equal(1, layout.FieldsPerStripe);
        Assert.All(layout.Columns, c => Assert.Equal(PanelColumnKind.Name, c.Kind));
        Assert.Equal(layout.Stripes - 1, layout.Separators.Count);
    }

    [Fact]
    public void FieldsAreDroppedFromTheRightWhenTheyNoLongerFit()
    {
        // Full needs 1 + (1+9) + (1+8) + (1+5) = 26 cells to show everything.
        Assert.Equal(4, PanelColumnLayout.Compute(PanelViewMode.Full, 26).FieldsPerStripe);
        Assert.Equal(3, PanelColumnLayout.Compute(PanelViewMode.Full, 25).FieldsPerStripe);
        Assert.Equal(2, PanelColumnLayout.Compute(PanelViewMode.Full, 19).FieldsPerStripe);
        Assert.Equal(1, PanelColumnLayout.Compute(PanelViewMode.Full, 10).FieldsPerStripe);
    }

    [Fact]
    public void WideDropsTheSizeColumnWhenItNoLongerFits()
    {
        // Name + size need 1 + (1 + 9) = 11 cells; one cell fewer and the size column goes,
        // leaving a stripe of nothing but names.
        Assert.Equal(2, PanelColumnLayout.Compute(PanelViewMode.Wide, 11).FieldsPerStripe);

        var layout = PanelColumnLayout.Compute(PanelViewMode.Wide, 10);
        Assert.Equal(1, layout.FieldsPerStripe);
        Assert.All(layout.Columns, c => Assert.Equal(PanelColumnKind.Name, c.Kind));
    }

    [Fact]
    public void ModesWithoutDataUseTheFullGeometry()
    {
        var full = PanelColumnLayout.Compute(PanelViewMode.Full, 44);
        var owners = PanelColumnLayout.Compute(PanelViewMode.FileOwners, 44);

        Assert.Equal(PanelViewMode.FileOwners, owners.Mode);
        Assert.Equal(PanelViewMode.Full, owners.EffectiveMode);
        Assert.Equal(full.Columns, owners.Columns);
        Assert.Equal(full.Separators, owners.Separators);
    }
}

public class PanelColumnLayoutInvariantTests
{
    public static TheoryData<PanelViewMode> Modes
    {
        get
        {
            var data = new TheoryData<PanelViewMode>();
            foreach (PanelViewMode mode in PanelViewModes.All)
            {
                data.Add(mode);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void ColumnsAndSeparatorsExactlyTileTheInnerWidth(PanelViewMode mode)
    {
        for (int inner = 1; inner <= 200; inner++)
        {
            var layout = PanelColumnLayout.Compute(mode, inner);
            var owner = new int[inner];
            Array.Fill(owner, -1);

            foreach (PanelColumn column in layout.Columns)
            {
                Assert.InRange(column.X, 0, inner - 1);
                Assert.InRange(column.Right, 1, inner);
                for (int i = column.X; i < column.Right; i++)
                {
                    Assert.Equal(-1, owner[i]);
                    owner[i] = 0;
                }
            }

            foreach (int separator in layout.Separators)
            {
                Assert.InRange(separator, 0, inner - 1);
                Assert.Equal(-1, owner[separator]);
                owner[separator] = 1;
            }

            for (int i = 0; i < inner; i++)
            {
                Assert.True(owner[i] >= 0, $"{mode} at inner width {inner}: cell {i} belongs to nothing.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void NameColumnsAreNeverSqueezedBelowOneCell(PanelViewMode mode)
    {
        for (int inner = 1; inner <= 200; inner++)
        {
            var layout = PanelColumnLayout.Compute(mode, inner);
            foreach (PanelColumn column in layout.Columns)
            {
                if (column.IsName)
                {
                    Assert.True(column.Width >= PanelColumn.MinNameWidth);
                }
                else
                {
                    Assert.Equal(PanelColumn.FixedWidthOf(column.Kind), column.Width);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void SeparatorsAreAscendingAndUnique(PanelViewMode mode)
    {
        for (int inner = 1; inner <= 120; inner++)
        {
            var separators = PanelColumnLayout.Compute(mode, inner).Separators;
            for (int i = 1; i < separators.Count; i++)
            {
                Assert.True(separators[i] > separators[i - 1]);
            }
        }
    }

    [Fact]
    public void StripeAtFindsTheStripeAndRejectsSeparators()
    {
        var layout = PanelColumnLayout.Compute(PanelViewMode.Medium, 38);

        Assert.Equal(0, layout.StripeAt(0));
        Assert.Equal(0, layout.StripeAt(18));
        Assert.Equal(-1, layout.StripeAt(19)); // the separator itself
        Assert.Equal(1, layout.StripeAt(20));
        Assert.Equal(1, layout.StripeAt(37));
        Assert.Equal(-1, layout.StripeAt(38)); // past the inner area
        Assert.Equal(-1, layout.StripeAt(-1));
    }

    [Fact]
    public void OffsetsMatchTheColumnsTheyDescribe()
    {
        var layout = PanelColumnLayout.Compute(PanelViewMode.Detailed, 55);

        Assert.Equal(layout.Columns.Count, layout.Offsets.Count);
        for (int i = 0; i < layout.Columns.Count; i++)
        {
            Assert.Equal(layout.Columns[i].X, layout.Offsets[i]);
        }
    }

    [Fact]
    public void IntraSeparatorsAreTheOnesInsideOneStripe()
    {
        // Wide's single stripe of name + size has one separator, and it sits inside the stripe.
        var wide = PanelColumnLayout.Compute(PanelViewMode.Wide, 40);

        Assert.Single(wide.Separators);
        Assert.Single(wide.IntraSeparators(0));
        Assert.Empty(wide.IntraSeparators(1));

        foreach (int sep in wide.IntraSeparators(0))
        {
            Assert.Contains(sep, wide.Separators);
        }

        // Medium's separator stands between its two stripes, so it is intra to neither.
        var medium = PanelColumnLayout.Compute(PanelViewMode.Medium, 38);

        Assert.Single(medium.Separators);
        Assert.Empty(medium.IntraSeparators(0));
        Assert.Empty(medium.IntraSeparators(1));
    }
}

public class PanelColumnTests
{
    [Fact]
    public void FixedWidthsAreTheContractedOnes()
    {
        Assert.Equal(9, PanelColumn.FixedWidthOf(PanelColumnKind.Size));
        Assert.Equal(8, PanelColumn.FixedWidthOf(PanelColumnKind.Date));
        Assert.Equal(5, PanelColumn.FixedWidthOf(PanelColumnKind.Time));
        Assert.Equal(5, PanelColumn.FixedWidthOf(PanelColumnKind.Attributes));
        Assert.Equal(0, PanelColumn.FixedWidthOf(PanelColumnKind.Name));
    }

    [Fact]
    public void HeadersAndAlignment()
    {
        Assert.Equal("Name", PanelColumn.HeaderOf(PanelColumnKind.Name));
        Assert.Equal("Size", PanelColumn.HeaderOf(PanelColumnKind.Size));
        Assert.Equal("Date", PanelColumn.HeaderOf(PanelColumnKind.Date));
        Assert.Equal("Time", PanelColumn.HeaderOf(PanelColumnKind.Time));
        Assert.Equal("Attr", PanelColumn.HeaderOf(PanelColumnKind.Attributes));

        Assert.Equal(HAlign.Right, PanelColumn.AlignOf(PanelColumnKind.Size));
        Assert.Equal(HAlign.Left, PanelColumn.AlignOf(PanelColumnKind.Name));
    }

    [Fact]
    public void RightIsExclusive()
    {
        var column = new PanelColumn(PanelColumnKind.Size, 1, 14, 9);
        Assert.Equal(23, column.Right);
        Assert.False(column.IsName);
    }
}
