using G923Mapper;

namespace G923Mapper.Tests;

public class CurvePointsTests
{
    private static double[][] Line() => CurvePoints.StraightLine();

    [Fact]
    public void A_new_curve_starts_as_a_straight_line()
    {
        Assert.Equal(2, Line().Length);
        Assert.Equal([0, 0], Line()[0]);
        Assert.Equal([1, 1], Line()[1]);
    }

    [Fact]
    public void A_point_is_added_in_x_order_and_the_original_is_not_changed()
    {
        var original = Line();
        var added = CurvePoints.Add(original, 0.5, 0.2, out int at)!;
        Assert.Equal(1, at);
        Assert.Equal(3, added.Length);
        Assert.Equal([0.5, 0.2], added[1]);
        Assert.Equal(2, original.Length);
    }

    [Fact]
    public void Points_keep_their_order_when_several_are_added_out_of_order()
    {
        var p = Line();
        p = CurvePoints.Add(p, 0.8, 0.5, out _)!;
        p = CurvePoints.Add(p, 0.3, 0.1, out int at)!;
        Assert.Equal(1, at);
        Assert.Equal(new[] { 0.0, 0.3, 0.8, 1.0 }, p.Select(pt => pt[0]));
    }

    [Theory]
    [InlineData(0.005)]     // too close to the first point
    [InlineData(0.995)]     // too close to the last
    public void A_point_with_no_room_is_refused(double x) => Assert.Null(CurvePoints.Add(Line(), x, 0.5, out _));

    [Fact]
    public void A_point_cannot_be_stacked_on_an_existing_one()
    {
        var p = CurvePoints.Add(Line(), 0.5, 0.5, out _)!;
        Assert.Null(CurvePoints.Add(p, 0.505, 0.9, out _));
        Assert.NotNull(CurvePoints.Add(p, 0.52, 0.9, out _));
    }

    [Fact]
    public void Adding_to_nothing_or_a_broken_curve_does_nothing()
    {
        Assert.Null(CurvePoints.Add(null, 0.5, 0.5, out _));
        Assert.Null(CurvePoints.Add([[0, 0]], 0.5, 0.5, out _));
    }

    [Fact]
    public void A_clicked_point_outside_the_graph_is_pulled_back_in()
    {
        var added = CurvePoints.Add(Line(), 0.5, 1.7, out _)!;
        Assert.Equal(1, added[1][1]);
    }

    [Fact]
    public void Dragging_moves_a_point_but_never_past_its_neighbours()
    {
        var p = CurvePoints.Add(CurvePoints.Add(Line(), 0.3, 0.3, out _)!, 0.7, 0.7, out _)!;   // 0, .3, .7, 1
        var moved = CurvePoints.Move(p, 1, 0.9, 0.4);
        Assert.Equal(0.7 - CurvePoints.MinGap, moved[1][0], 6);   // stops just short of the next point
        Assert.Equal(0.4, moved[1][1]);

        var back = CurvePoints.Move(p, 2, 0.0, 0.7);
        Assert.Equal(0.3 + CurvePoints.MinGap, back[2][0], 6);
    }

    [Fact]
    public void The_end_points_slide_up_and_down_but_stay_at_the_ends_of_the_range_or_inside_it()
    {
        var p = Line();
        var first = CurvePoints.Move(p, 0, -0.5, 0.25);
        Assert.Equal(0, first[0][0]);
        Assert.Equal(0.25, first[0][1]);
        var last = CurvePoints.Move(p, 1, 2.0, -0.3);
        Assert.Equal(1, last[1][0]);
        Assert.Equal(0, last[1][1]);
    }

    [Fact]
    public void Moved_points_always_stay_strictly_ascending_even_after_rounding()
    {
        var p = CurvePoints.Add(Line(), 0.5, 0.5, out _)!;
        var moved = CurvePoints.Rounded(CurvePoints.Move(p, 1, 0.99999, 0.5));
        for (int i = 1; i < moved.Length; i++) Assert.True(moved[i][0] > moved[i - 1][0]);
    }

    [Fact]
    public void A_point_can_be_removed_but_a_curve_keeps_two()
    {
        var three = CurvePoints.Add(Line(), 0.5, 0.2, out _)!;
        var two = CurvePoints.Remove(three, 1);
        Assert.Equal(2, two.Length);
        Assert.Equal(2, CurvePoints.Remove(two, 0).Length);   // refused
        Assert.Equal(3, three.Length);                         // the original is untouched
    }

    [Fact]
    public void The_result_always_loads_as_a_valid_custom_curve()
    {
        var p = Line();
        p = CurvePoints.Add(p, 0.25, 0.1, out _)!;
        p = CurvePoints.Add(p, 0.75, 0.9, out _)!;
        p = CurvePoints.Move(p, 2, 0.6, 0.55);
        string json = "{ \"name\": \"t\", \"rules\": [ { \"from\": \"throttle\", \"to\": \"RightTrigger\", \"curve\": \"custom\", \"points\": [" +
            string.Join(", ", CurvePoints.Rounded(p).Select(pt => $"[{pt[0]:0.###}, {pt[1]:0.###}]")) + "] } ] }";
        var r = ProfileLoader.Parse(json, DeviceRegistry.CreateDefault());
        Assert.True(r.Ok, string.Join("; ", r.Errors));
    }
}
