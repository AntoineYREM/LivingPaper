using System.Xml.Linq;
using Shouldly;

namespace LivingPaper.Evals.Tests;

public class ChartSvgTests
{
    private static readonly QualityRow[] Quality = [new("DeepSeek V4.1 Flash", 4.35, 4.92), new("GPT-5 mini", 3.58, 4.5)];

    private static readonly ValuePoint[] Value =
    [
        new("Free One", 4.83, 0),
        new("Cheap & Good <1>", 4.92, 0.09),
        new("Costly", 4.9, 49.63),
        new("No Price", 3.0, null),
    ];

    private static readonly LatencyBar[] Latency = [new("Fast", 2.14, 1.38), new("Slow", 11.55, 10.82), new("Unknown First Text", 5.0, null)];

    private static XDocument Parse(string svg) => XDocument.Parse(svg);

    private static string AllText(XDocument document) => string.Concat(document.Descendants().Where(element => element.Name.LocalName == "text").Select(element => element.Value + "|"));

    [Fact]
    public void Quality_WritesEachRowWithItsTwoGrades_InTwoDecimals()
    {
        var document = Parse(ChartSvg.Quality(Quality, ChartTheme.Light));

        var text = AllText(document);
        text.ShouldContain("DeepSeek V4.1 Flash|");
        text.ShouldContain("4.35|");
        text.ShouldContain("4.92|");
        text.ShouldContain("4.50|");
        text.ShouldContain("The axis starts at 3.");
        document.Root!.Descendants().Count(element => element.Name.LocalName == "circle").ShouldBe(2 * Quality.Length + 2); // two dots a row, two in the legend
    }

    [Fact]
    public void Quality_MovesTheStartOfTheAxisDown_WhenAGradeIsBelowIt()
    {
        var text = AllText(Parse(ChartSvg.Quality([new QualityRow("Low", 1.77, 3.2)], ChartTheme.Light)));

        text.ShouldContain("The axis starts at 1.5.");
    }

    [Fact]
    public void Value_PutsFreeModelsInTheirOwnBand_AndLeavesOutTheOnesWithNoPrice()
    {
        var document = Parse(ChartSvg.Value(Value, "USD", ChartTheme.Light));

        var text = AllText(document);
        text.ShouldContain("Free One · 4.83|");
        text.ShouldContain("Costly · 4.90|");
        text.ShouldNotContain("No Price ·");
        text.ShouldContain("0.01|"); // the logarithmic axis goes down to a decade under the cheapest paid model
        text.ShouldContain("100|");
        document.Root!.Descendants().Single(element => element.Name.LocalName == "desc").Value.ShouldContain("No Price: grade 3.00, no price");
    }

    [Fact]
    public void Value_EscapesWhatIsInANameOfAModel() =>
        AllText(Parse(ChartSvg.Value(Value, "USD", ChartTheme.Dark))).ShouldContain("Cheap & Good <1> · 4.92|");

    [Fact]
    public void Latency_DrawsTwoPartsForABarWithAFirstText_AndOneWithout()
    {
        var document = Parse(ChartSvg.Latency(Latency, ChartTheme.Light));

        var text = AllText(document);
        text.ShouldContain("2.14 s · first text 1.38 s|");
        text.ShouldContain("5.00 s|"); // no first text recorded: the total only
        text.ShouldContain("12 s|"); // the axis goes up to the slowest, rounded up to a multiple of 2
        var bars = document.Root!.Descendants().Where(element => element.Name.LocalName == "rect" && element.Attribute("rx")?.Value == "3").ToList();
        bars.Count.ShouldBe(5); // 2 + 2 + 1
    }

    [Theory]
    [InlineData("quality")]
    [InlineData("value")]
    [InlineData("latency")]
    public void EachChart_HasItsOwnColorsInTheLightAndTheDarkTheme_AndNoBrokenNumber(string chart)
    {
        string Draw(ChartTheme theme) => chart switch
        {
            "quality" => ChartSvg.Quality(Quality, theme),
            "value" => ChartSvg.Value(Value, "USD", theme),
            _ => ChartSvg.Latency(Latency, theme),
        };

        var light = Draw(ChartTheme.Light);
        var dark = Draw(ChartTheme.Dark);

        light.ShouldContain(ChartTheme.Light.Surface);
        light.ShouldNotContain(ChartTheme.Dark.Surface);
        dark.ShouldContain(ChartTheme.Dark.Surface);
        foreach (var svg in new[] { light, dark })
        {
            svg.ShouldNotContain("NaN");
            svg.ShouldNotContain("Infinity");
            Parse(svg).Root!.Attribute("role")!.Value.ShouldBe("img");
            Parse(svg).Descendants().Single(element => element.Name.LocalName == "title").Value.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void TheChartsDoNotBreakWithNoData()
    {
        foreach (var theme in new[] { ChartTheme.Light, ChartTheme.Dark })
        {
            Parse(ChartSvg.Quality([], theme));
            Parse(ChartSvg.Value([], "USD", theme));
            Parse(ChartSvg.Latency([], theme));
        }
    }

    [Fact]
    public void Value_KeepsTheLabelsOfModelsThatAreCloseApartFromEachOther()
    {
        ValuePoint[] close =
        [
            new("DeepSeek V4.1 Flash", 4.92, 0.09),
            new("Claude Haiku 5.5", 4.35, 1.2),
            new("GPT-5 mini", 4.50, 1.79),
            new("Claude Opus 5.5", 4.90, 49.6),
        ];

        var document = Parse(ChartSvg.Value(close, "USD", ChartTheme.Light));

        // A label is about 6.4 pixels a character; two labels that are within a text height of each other must not share any horizontal space.
        var labels = document.Descendants()
            .Where(element => element.Name.LocalName == "text" && element.Value.Contains(" · ", StringComparison.Ordinal))
            .Select(element =>
            {
                var x = double.Parse(element.Attribute("x")!.Value, System.Globalization.CultureInfo.InvariantCulture);
                var y = double.Parse(element.Attribute("y")!.Value, System.Globalization.CultureInfo.InvariantCulture);
                var width = element.Value.Length * 6.4;
                return element.Attribute("text-anchor")!.Value == "end" ? (Left: x - width, Right: x, Y: y) : (Left: x, Right: x + width, Y: y);
            })
            .ToList();
        labels.Count.ShouldBe(4);
        for (var first = 0; first < labels.Count; first++)
        {
            for (var second = first + 1; second < labels.Count; second++)
            {
                var overlap = Math.Abs(labels[first].Y - labels[second].Y) < 12 && labels[first].Left < labels[second].Right && labels[first].Right > labels[second].Left;
                overlap.ShouldBeFalse($"labels {first} and {second} overlap");
            }
        }

        labels.ShouldAllBe(label => label.Right <= 720);
    }
}
