using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>ModelTests: tests from ModelTests.Timeline.cs (converted from the Godot model tests).</summary>
public partial class ModelTests
{
    /// <summary>The playback timeline: cue fades, actor steps, holds, key frames.</summary>
    [Fact]
    public void TimelineTest()
    {
        var show = MagicQShow.Load(Show("RT2025_031.shw"));
        var stack = show.CueStacks.First(s => s.Name == "show");
        var blocking = new Blocking();
        blocking.Actors.Add(new ActorDef { Id = "w", Name = "Walker", Initial = new ActorState { Visible = true, Pose = ActorPose.Standing, Position = new[] { 0f, 0f, -3f }, Facing = 0f } });
        blocking.Actors.Add(new ActorDef { Id = "n", Name = "Newcomer", Initial = new ActorState { Visible = false, Pose = ActorPose.Standing, Position = new[] { -2f, 0f, -4f }, Facing = 0f } });
        var steps = stack.Steps;
        int start = steps.First(s => s.CueNumberText == "1").CueId, next = steps.First(s => s.CueNumberText == "1.1").CueId;
        int iStart = steps.ToList().FindIndex(s => s.CueId == start);
        // Two movement steps in cue "1": walk 2 m in 2 s, wait 1 s, walk on 1.2 m at walking speed and sit facing 90°.
        blocking.Record(start, "w", 0, new ActorState { Position = new[] { 2f, 0f, -3f }, MoveTime = 2f });
        blocking.Record(start, "w", 1, new ActorState { Delay = 1f, Position = new[] { 2f, 0f, -4.2f }, Pose = ActorPose.Seated, Facing = 90f });
        blocking.Record(start, "n", 0, new ActorState { Delay = 0.5f, Visible = true });
        blocking.Holds[start] = 4f;
        var tl = new PlaybackTimeline(show, stack, blocking, 3f);

        var seg = tl.Segments[iStart];
        double walkEnd = seg.Start + 2 + 0.6 + 1 + 1.2 / PlaybackTimeline.WalkSpeed + 0.6;
        Check(Math.Abs(seg.ActorsEnd - (walkEnd - seg.Start)) < 1e-6 && Math.Abs(seg.End - (seg.ActiveEnd + 4)) < 1e-6,
              $"cue 1: actors take {seg.ActorsEnd:0.00} s, hold 4 s");
        Check(Math.Abs(seg.ActiveEnd - seg.Start - Math.Max(Math.Max(seg.FadeIn, seg.FadeOut), seg.ActorsEnd)) < 1e-6
              && Math.Abs(tl.Segments[iStart + 1].Start - seg.End) < 1e-6, "next GO = end of the cue + hold");
        Check(tl.Segments.Where(s => s.CueId != start).All(s => Math.Abs(s.Hold - 3f) < 1e-6), "other cues use the default hold");

        // Tracking: before the first GO all off; at the end of a cue = MagicQ's tracked state.
        Check(tl.DmxAt(-1).Count == 0, "before the first GO nothing is on");
        var endOfCue = tl.DmxAt(seg.ActiveEnd);
        var tracked = new System.Collections.Generic.Dictionary<int, int>();
        foreach (var st in steps.Take(iStart + 1))
            foreach (var (a, v) in show.Cues.First(c => c.Id == st.CueId).Channels)
                tracked[a] = show.Resolve(a, v);
        Check(tracked.All(kv => endOfCue.GetValueOrDefault(kv.Key) == kv.Value), "end of a cue = tracked state");

        // Mid-fade: a channel rising from 0 is halfway after half the In time.
        var before = tl.DmxAt(seg.Start - 0.01);
        var rising = endOfCue.First(kv => kv.Value > before.GetValueOrDefault(kv.Key) + 50);
        int mid = tl.DmxAt(seg.Start + seg.FadeIn / 2).GetValueOrDefault(rising.Key);
        int expected = (before.GetValueOrDefault(rising.Key) + rising.Value) / 2;
        Check(Math.Abs(mid - expected) <= 2, $"mid-fade level {mid} (expected ~{expected})");

        // Actors: mid-walk, wait, end; appearing; scrubbing back = playing forward.
        var w1 = tl.ActorAt("w", seg.Start + 1);
        Check(w1.Visible && w1.Position.X > 0.5f && w1.Position.X < 1.5f && w1.Pose == ActorPose.Standing, $"mid-walk at x {w1.Position.X:0.00}");
        var wWait = tl.ActorAt("w", seg.Start + 2 + 0.6 + 0.5);
        Check(Vector3.Distance(wWait.Position, new Vector3(2, 0, -3)) < 1e-4f, "waits at the first waypoint");
        var wEnd = tl.ActorAt("w", walkEnd + 0.01);
        Check(Vector3.Distance(wEnd.Position, new Vector3(2, 0, -4.2f)) < 1e-4f && wEnd.Pose == ActorPose.Seated && Math.Abs(wEnd.Facing - 90f) < 0.01f,
              "seated at the second waypoint facing 90°");
        Check(!tl.ActorAt("n", seg.Start + 0.4).Visible && tl.ActorAt("n", seg.Start + 0.6).Visible, "appears after its 0.5 s wait");
        double probe = seg.Start + 1.3;
        var forward = tl.ActorAt("w", probe);
        tl.ActorAt("w", seg.End + 20);
        Check(tl.ActorAt("w", probe) == forward && tl.DmxAt(probe).SequenceEqual(tl.DmxAt(probe)), "scrubbing back gives the same state");

        // Entrances: the newcomer comes on jardin (x -2) after its wait; the walker goes off cour in the next cue.
        blocking.Record(next, "w", 0, new ActorState { Position = new[] { 5f, 0f, -4.2f }, Pose = ActorPose.Standing, Visible = false });
        tl = new PlaybackTimeline(show, stack, blocking, 3f);
        var entrances = tl.Entrances();
        Check(entrances.Count == 2 && entrances[0] is { ActorId: "n", On: true, Side: StageSide.Jardin, CueNumber: "1" }
              && Math.Abs(entrances[0].Time - (seg.Start + 0.5)) < 1e-6 && entrances[0].Text == "OP jardin",
              $"newcomer OP jardin in cue 1: {string.Join(", ", entrances)}");
        var off = entrances[1];
        var nextSeg = tl.Segments[iStart + 1];
        Check(off is { ActorId: "w", On: false, Side: StageSide.Cour, CueNumber: "1.1" } && off.Text == "AF cour"
              && Math.Abs(off.Time - tl.Steps.First(s => s.ActorId == "w" && s.Segment == iStart + 1).End) < 1e-6
              && off.Time > nextSeg.Start, $"walker AF cour at the end of its walk in cue 1.1: {off}");
        Check(tl.Entrances("n").Count == 1, "one actor's entrances");
        Check(ActorEntrance.SideOf(new Vector3(0.3f, 0, -5)) == StageSide.Lointain && ActorEntrance.SideOf(new Vector3(-0.3f, 0, 2)) == StageSide.Face,
              "near the centre line: lointain / face");
        blocking.ClearCue(next, "w");
        tl = new PlaybackTimeline(show, stack, blocking, 3f);

        var keys = tl.KeyFrames();
        Check(keys.Count >= tl.Segments.Count && keys.Zip(keys.Skip(1)).All(p => p.Second.Time - p.First.Time >= 0.05)
              && keys.Any(k => k.What.Contains("w step 2 done")), $"{keys.Count} key frames, sorted, steps included");
    }

    /// <summary>
    /// Smoke per cue: the haze stays at the base before any cue sets smoke, a ramp from a cue's GO eases to its amount in
    /// its time, a later cue starts from where the haze is (also mid-ramp), cues without smoke keep it (tracking), and
    /// a jump to a cue gives the settled amount.
    /// </summary>
    [Fact]
    public void SmokeTimelineTest()
    {
        var show = MagicQShow.Load(Show("RT2025_031.shw"));
        var stack = show.CueStacks.First(s => s.Name == "show");
        var ids = stack.Steps.Select(s => s.CueId).ToList();
        var blocking = new Blocking { Holds = { [ids[1]] = 10f } };
        float baseHaze = 0.02f;
        var none = new PlaybackTimeline(show, stack, blocking, 3f, baseHaze);
        Check(!none.HasSmoke && none.HazeAt(5) == baseHaze && none.HazeAfter(3) == baseHaze, "no cue sets smoke: the haze stays at the base");

        // Cue 2: 50 % in 8 s; cue 4: 0 % in 2 s, starting while cue 2's ramp is still going.
        blocking.Smoke[ids[1]] = new CueSmoke { Amount = 50, Time = 8f };
        blocking.Smoke[ids[3]] = new CueSmoke { Amount = 0, Time = 2f };
        var tl = new PlaybackTimeline(show, stack, blocking, 3f, baseHaze);
        var go2 = tl.Segments[1];
        float full = ProjectLook.DensityOf(50);
        Check(tl.HasSmoke && tl.HazeAt(go2.Start - 0.1) == baseHaze && Math.Abs(tl.HazeAt(go2.Start) - baseHaze) < 1e-6f
              && Math.Abs(tl.HazeAt(go2.Start + 4) - (baseHaze + full) / 2) < 1e-5f && Math.Abs(tl.HazeAt(go2.Start + 8) - full) < 1e-6f
              && Math.Abs(tl.HazeAt(go2.Start + 9) - full) < 1e-6f, "a ramp eases from the base to the amount (half way at half the time)");
        float quarter = baseHaze + (full - baseHaze) * 0.25f;
        Check(tl.HazeAt(go2.Start + 2) < quarter && tl.HazeAt(go2.Start + 6) > baseHaze + (full - baseHaze) * 0.75f, "slow at the ends (eased)");
        Check(Math.Abs(tl.HazeAt(tl.Segments[2].Start) - full) < 1e-6f, "a cue without smoke keeps it (tracking)");
        var go4 = tl.Segments[3];
        Check(go4.Start - go2.Start > 8 && Math.Abs(tl.HazeAt(go4.Start) - full) < 1e-6f, "cue 4 starts from the finished 50 %");
        Check(Math.Abs(tl.HazeAt(go4.Start + 1) - full / 2) < 1e-5f && tl.HazeAt(go4.Start + 2) == 0f, "and thins to 0 in 2 s");
        Check(tl.HazeAfter(0) == baseHaze && Math.Abs(tl.HazeAfter(1) - full) < 1e-6f && Math.Abs(tl.HazeAfter(2) - full) < 1e-6f
              && tl.HazeAfter(3) == 0f && tl.HazeAfter(-1) == baseHaze, "the settled haze after a cue");
        Check(tl.SmokeOf(1) == 50 && tl.SmokeOf(3) == 0 && tl.SmokeOf(2) == null, "the amount a cue sets");
        Check(tl.KeyFrames().Any(k => k.What.Contains("smoke 50 % reached")), "a key frame when a ramp is done");

        // A GO while the earlier ramp is not finished goes on from where the haze is.
        var quick = new Blocking { Holds = { [ids[1]] = 1f } };
        quick.Smoke[ids[1]] = new CueSmoke { Amount = 100, Time = 20f };
        quick.Smoke[ids[2]] = new CueSmoke { Amount = 0, Time = 20f };
        var q = new PlaybackTimeline(show, stack, quick, 3f, baseHaze);
        float mid = q.HazeAt(q.Segments[2].Start);
        Check(mid > baseHaze && mid < ProjectLook.DensityOf(100) && Math.Abs(q.HazeAt(q.Segments[2].Start + 0.001) - mid) < 1e-4f,
              $"a cue that goes while the haze is still building starts from {mid:0.0000}, not from the target");
        Check(ProjectLook.PercentOf(ProjectLook.DensityOf(37)) is > 36.99 and < 37.01 && ProjectLook.DensityOf(250) == ProjectLook.MaxHaze,
              "amount <-> density");
    }
}
