using System.Globalization;
using System.Security;
using System.Text;

namespace LivingPaper.Evals;

// The colors of a chart for one theme. The blue and the orange are the first two colors of the project's palette for charts (checked for
// color-blind readers and contrast with the validator of the dataviz skill, in the light and in the dark mode); Strong and Soft are two steps
// of the blue, for a bar that has two parts; Gray is for what is "before". The surface is part of the picture, so it reads on any page.
internal sealed record ChartTheme(string Name, string Surface, string Ink, string InkSecondary, string Grid, string Blue, string Orange, string Gray, string Strong, string Soft)
{
    public static readonly ChartTheme Light = new("light", "#fcfcfb", "#0b0b0b", "#52514e", "#e3e2de", "#2a78d6", "#eb6834", "#8e8d87", "#184f95", "#6da7ec");

    public static readonly ChartTheme Dark = new("dark", "#1a1a19", "#f0efec", "#c3c2b7", "#343432", "#3987e5", "#d95926", "#9b9a93", "#86b6ef", "#256abf");
}

// The three charts of the README, as SVG text: nothing to run to see them, they stay sharp at any size and GitHub shows them in its own theme
// (one file per theme). Every chart has its title, a sentence under it, a legend, the values written next to the marks, and a description
// for screen readers that lists the figures.
internal static class ChartSvg
{
    private const int Width = 720;
    private const string Font = "system-ui, -apple-system, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif";

    // Before and after: one row per model, a dot for the grade with the first prompt, a dot for the current one, and a line between them.
    public static string Quality(IReadOnlyList<QualityRow> rows, ChartTheme theme)
    {
        const double left = 200;
        const double right = 660;
        const double top = 100;
        const double rowHeight = 44;

        var low = rows.Count == 0 ? 3.0 : Math.Min(3.0, Math.Floor(rows.Min(row => Math.Min(row.Before, row.After)) * 2) / 2);
        const double high = 5.0;
        double X(double grade) => left + ((Math.Clamp(grade, low, high) - low) / (high - low) * (right - left));
        var bottom = top + (rows.Count * rowHeight);
        var height = (int)bottom + 56;

        var svg = Begin(
            height,
            "Average grade before and after the change of prompt",
            string.Join("; ", rows.Select(row => $"{row.Label}: {Number(row.Before)} before, {Number(row.After)} after")),
            theme);
        Text(svg, 24, 32, "Average grade before and after the change of prompt", 17, theme.Ink, weight: "600");
        Text(svg, 24, 54, $"Grade from 1 to 5 given by the judge, mean of the answers. The axis starts at {Plain(low)}.", 12, theme.InkSecondary);
        Dot(svg, 28, 78, theme.Gray, theme.Surface);
        Text(svg, 40, 82, "with the first prompt", 12, theme.InkSecondary);
        Dot(svg, 190, 78, theme.Blue, theme.Surface);
        Text(svg, 202, 82, "with the current prompt", 12, theme.InkSecondary);

        for (var grade = low; grade <= high + 0.001; grade += 0.5)
        {
            Line(svg, X(grade), top - 4, X(grade), bottom, theme.Grid, 1);
            Text(svg, X(grade), bottom + 20, grade.ToString("0.0", CultureInfo.InvariantCulture), 11, theme.InkSecondary, "middle");
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var y = top + (index * rowHeight) + (rowHeight / 2);
            Text(svg, left - 14, y + 4, row.Label, 13, theme.Ink, "end");
            Line(svg, X(row.Before), y, X(row.After), y, theme.Gray, 2);
            Dot(svg, X(row.Before), y, theme.Gray, theme.Surface);
            Dot(svg, X(row.After), y, theme.Blue, theme.Surface);
            var afterIsHigher = row.After >= row.Before;
            Text(svg, X(row.Before) + (afterIsHigher ? -12 : 12), y + 4, Number(row.Before), 12, theme.Ink, afterIsHigher ? "end" : "start");
            Text(svg, X(row.After) + (afterIsHigher ? 12 : -12), y + 4, Number(row.After), 12, theme.Ink, afterIsHigher ? "start" : "end", "600");
        }

        return End(svg);
    }

    // Grade and price: one dot per model. Free and local models have no price to put on a logarithmic axis, so they stand in a band of their own.
    public static string Value(IReadOnlyList<ValuePoint> points, string currency, ChartTheme theme)
    {
        const double plotLeft = 56;
        const double bandX = 84;
        const double separatorX = 250;
        const double axisLeft = 270;
        const double axisRight = 690;
        const double top = 104;
        const double bottom = 364;

        var paid = points.Where(point => point.CostPer1000 is > 0).ToList();
        var free = points.Where(point => point.IsFree).ToList();
        var yLow = points.Count == 0 ? 1.5 : Math.Min(1.5, Math.Floor(points.Min(point => point.Score) * 2) / 2);
        const double yHigh = 5.0;
        double Y(double grade) => top + ((yHigh - grade) / (yHigh - yLow) * (bottom - top));

        var decadeLow = paid.Count == 0 ? -1 : (int)Math.Floor(Math.Log10(paid.Min(point => point.CostPer1000!.Value)));
        var decadeHigh = paid.Count == 0 ? 2 : (int)Math.Ceiling(Math.Log10(paid.Max(point => point.CostPer1000!.Value)));
        if (decadeHigh <= decadeLow)
        {
            decadeHigh = decadeLow + 1;
        }

        double X(double cost) => axisLeft + ((Math.Log10(cost) - decadeLow) / (decadeHigh - decadeLow) * (axisRight - axisLeft));

        var svg = Begin(
            430,
            "Grade and price of a reply, per model",
            string.Join("; ", points.Select(point => $"{point.Name}: grade {Number(point.Score)}, " + (point.IsFree ? "free or local" : point.CostPer1000 is { } cost ? $"{Money(cost)} {currency} for 1,000 replies" : "no price"))),
            theme);
        Text(svg, 24, 32, "Grade and price of a reply, per model", 17, theme.Ink, weight: "600");
        Text(svg, 24, 54, $"Price of the chat for 1,000 replies in {currency} (logarithmic axis). The grade axis starts at {Plain(yLow)}.", 12, theme.InkSecondary);
        Dot(svg, 28, 78, theme.Orange, theme.Surface);
        Text(svg, 40, 82, "free or local", 12, theme.InkSecondary);
        Dot(svg, 150, 78, theme.Blue, theme.Surface);
        Text(svg, 162, 82, "paid API", 12, theme.InkSecondary);

        for (var grade = Math.Ceiling(yLow); grade <= yHigh + 0.001; grade += 1)
        {
            Line(svg, plotLeft, Y(grade), axisRight, Y(grade), theme.Grid, 1);
            Text(svg, plotLeft - 8, Y(grade) + 4, grade.ToString("0", CultureInfo.InvariantCulture), 11, theme.InkSecondary, "end");
        }

        for (var decade = decadeLow; decade <= decadeHigh; decade++)
        {
            var cost = Math.Pow(10, decade);
            Line(svg, X(cost), top, X(cost), bottom, theme.Grid, 1);
            Text(svg, X(cost), bottom + 20, Money(cost), 11, theme.InkSecondary, "middle");
        }

        Line(svg, separatorX, top - 6, separatorX, bottom + 6, theme.InkSecondary, 1, dash: "3 4");
        Text(svg, (plotLeft + separatorX) / 2, bottom + 20, "free or local", 11, theme.InkSecondary, "middle");
        Text(svg, (axisLeft + axisRight) / 2, bottom + 44, $"price for 1,000 replies ({currency})", 12, theme.InkSecondary, "middle");
        Text(svg, 14, (top + bottom) / 2, "grade", 12, theme.InkSecondary, "middle", rotate: -90);

        foreach (var point in free)
        {
            Dot(svg, bandX, Y(point.Score), theme.Orange, theme.Surface);
            Text(svg, bandX + 12, Y(point.Score) + 4, $"{point.Name} · {Number(point.Score)}", 12, theme.Ink);
        }

        foreach (var point in paid)
        {
            var x = X(point.CostPer1000!.Value);
            var labelOnTheLeft = x > axisRight - 130;
            Dot(svg, x, Y(point.Score), theme.Blue, theme.Surface);
            // A point near the right edge has its label above it, aligned to its right: it cannot run off the picture, and it does not meet the
            // label of a neighbour at the same height.
            Text(svg, x + (labelOnTheLeft ? 6 : 12), Y(point.Score) + (labelOnTheLeft ? -12 : 4), $"{point.Name} · {Number(point.Score)}", 12, theme.Ink, labelOnTheLeft ? "end" : "start");
        }

        return End(svg);
    }

    // Time to answer: one bar per model, fastest first, in two parts: the wait until the first text, then the rest of the reply.
    public static string Latency(IReadOnlyList<LatencyBar> bars, ChartTheme theme)
    {
        const double left = 200;
        const double plotWidth = 330;
        const double top = 104;
        const double rowHeight = 30;
        const double barHeight = 16;

        var scaleMax = bars.Count == 0 ? 2.0 : Math.Max(2.0, Math.Ceiling(bars.Max(bar => bar.TotalSeconds) / 2) * 2);
        double X(double seconds) => left + (seconds / scaleMax * plotWidth);
        var bottom = top + (bars.Count * rowHeight);
        var height = (int)bottom + 52;

        var svg = Begin(
            height,
            "Time to answer, per model",
            string.Join("; ", bars.Select(bar => $"{bar.Name}: {Number(bar.TotalSeconds)} seconds in all" + (bar.FirstTextSeconds is { } first ? $", {Number(first)} seconds until the first text" : string.Empty))),
            theme);
        Text(svg, 24, 32, "Time to answer, per model", 17, theme.Ink, weight: "600");
        Text(svg, 24, 54, "Mean of the chat call as Langfuse recorded it, the judge not counted. The latest run of each model.", 12, theme.InkSecondary);
        Square(svg, 24, 70, theme.Strong);
        Text(svg, 40, 82, "until the first text", 12, theme.InkSecondary);
        Square(svg, 170, 70, theme.Soft);
        Text(svg, 186, 82, "until the end of the reply", 12, theme.InkSecondary);

        for (var seconds = 0.0; seconds <= scaleMax + 0.001; seconds += 2)
        {
            Line(svg, X(seconds), top - 6, X(seconds), bottom, theme.Grid, 1);
            Text(svg, X(seconds), bottom + 20, $"{Plain(seconds)} s", 11, theme.InkSecondary, "middle");
        }

        for (var index = 0; index < bars.Count; index++)
        {
            var bar = bars[index];
            var y = top + (index * rowHeight) + ((rowHeight - barHeight) / 2);
            Text(svg, left - 12, y + 12, bar.Name, 13, theme.Ink, "end");
            if (bar.FirstTextSeconds is { } first && first > 0 && first < bar.TotalSeconds)
            {
                // The two parts are separated by a 2 pixel gap of the color of the surface.
                Bar(svg, left, y, X(first) - left, barHeight, theme.Strong);
                Bar(svg, X(first) + 2, y, X(bar.TotalSeconds) - X(first) - 2, barHeight, theme.Soft);
            }
            else
            {
                Bar(svg, left, y, X(bar.TotalSeconds) - left, barHeight, theme.Soft);
            }

            var detail = bar.FirstTextSeconds is { } shown ? $"{Number(bar.TotalSeconds)} s · first text {Number(shown)} s" : $"{Number(bar.TotalSeconds)} s";
            Text(svg, X(bar.TotalSeconds) + 8, y + 12, detail, 12, theme.Ink);
        }

        return End(svg);
    }

    // A grade or a time with two decimals, written as the reports of the tool write them (F2), so the pictures and the tables agree.
    private static string Number(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    // A whole number of a scale (the axis starts at 3, 1.5...): no useless decimals.
    private static string Plain(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Money(double value) => value >= 1 ? value.ToString("0.##", CultureInfo.InvariantCulture) : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static StringBuilder Begin(int height, string title, string description, ChartTheme theme)
    {
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {height}\" width=\"{Width}\" height=\"{height}\" role=\"img\" aria-labelledby=\"title desc\" font-family=\"{Font}\">\n");
        svg.Append("  <title id=\"title\">").Append(SecurityElement.Escape(title)).Append("</title>\n");
        svg.Append("  <desc id=\"desc\">").Append(SecurityElement.Escape(description)).Append("</desc>\n");
        svg.Append(CultureInfo.InvariantCulture, $"  <rect width=\"{Width}\" height=\"{height}\" rx=\"10\" fill=\"{theme.Surface}\"/>\n");
        return svg;
    }

    private static string End(StringBuilder svg) => svg.Append("</svg>\n").ToString();

    private static void Text(StringBuilder svg, double x, double y, string text, int size, string fill, string anchor = "start", string? weight = null, int rotate = 0)
    {
        svg.Append(CultureInfo.InvariantCulture, $"  <text x=\"{Coordinate(x)}\" y=\"{Coordinate(y)}\" font-size=\"{size}\" fill=\"{fill}\" text-anchor=\"{anchor}\"");
        if (weight is not null)
        {
            svg.Append(CultureInfo.InvariantCulture, $" font-weight=\"{weight}\"");
        }

        if (rotate != 0)
        {
            svg.Append(CultureInfo.InvariantCulture, $" transform=\"rotate({rotate} {Coordinate(x)} {Coordinate(y)})\"");
        }

        svg.Append('>').Append(SecurityElement.Escape(text)).Append("</text>\n");
    }

    private static void Line(StringBuilder svg, double x1, double y1, double x2, double y2, string stroke, int width, string? dash = null) =>
        svg.Append(CultureInfo.InvariantCulture, $"  <line x1=\"{Coordinate(x1)}\" y1=\"{Coordinate(y1)}\" x2=\"{Coordinate(x2)}\" y2=\"{Coordinate(y2)}\" stroke=\"{stroke}\" stroke-width=\"{width}\"")
            .Append(dash is null ? string.Empty : $" stroke-dasharray=\"{dash}\"")
            .Append("/>\n");

    // A dot with a ring of the color of the surface, so that two dots that touch stay apart.
    private static void Dot(StringBuilder svg, double x, double y, string fill, string ring) =>
        svg.Append(CultureInfo.InvariantCulture, $"  <circle cx=\"{Coordinate(x)}\" cy=\"{Coordinate(y)}\" r=\"6\" fill=\"{fill}\" stroke=\"{ring}\" stroke-width=\"2\"/>\n");

    private static void Square(StringBuilder svg, double x, double y, string fill) =>
        svg.Append(CultureInfo.InvariantCulture, $"  <rect x=\"{Coordinate(x)}\" y=\"{Coordinate(y)}\" width=\"12\" height=\"12\" rx=\"2\" fill=\"{fill}\"/>\n");

    private static void Bar(StringBuilder svg, double x, double y, double width, double height, string fill) =>
        svg.Append(CultureInfo.InvariantCulture, $"  <rect x=\"{Coordinate(x)}\" y=\"{Coordinate(y)}\" width=\"{Coordinate(Math.Max(width, 1))}\" height=\"{Coordinate(height)}\" rx=\"3\" fill=\"{fill}\"/>\n");

    private static string Coordinate(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
