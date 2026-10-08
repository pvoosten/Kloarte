using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kloarte.Model;

namespace Kloarte.MagicQ;

internal sealed partial class MagicQExporter
{
    /// <summary>
    /// The cue list as its cue stack (a new one copied from a stack record when the show hasn't it): steps of cues the
    /// design hasn't any more are removed (their cue records too, unless another stack plays them); a cue's channels,
    /// times, text and name are set where they differ; a renumbered cue's step is taken out and put in at its number;
    /// a new cue gets a cue record (copied from the simplest one) and a step. New cues get their MagicQ id in the design.
    /// </summary>
    private void ExportCues()
    {
        int number = _design.CueList.Stack ?? 1;
        var stack = _show.CueStacks.FirstOrDefault(s => s.Number == number);
        if (stack == null && _design.CueOrder.Count == 0)
            return;
        stack ??= NewStack(number);
        _design.CueList.Stack = number;
        if (_design.CueList.Name is { Length: > 0 } name && stack.Name != Clean(name))
        {
            stack.Name = Clean(name);
            Wrote($"cue stack {number} named \"{name}\"");
        }
        var otherIds = _show.CueStacks.Where(s => s != stack).SelectMany(s => s.Steps.Select(x => x.CueId)).ToHashSet();
        var designIds = _design.CueOrder.Where(c => c.Id != null).Select(c => c.Id.Value).ToHashSet();
        foreach (var step in stack.Steps.Where(s => !designIds.Contains(s.CueId)).ToList())
        {
            stack.RemoveStep(step.CueId);
            if (!otherIds.Contains(step.CueId) && _show.Cues.FirstOrDefault(c => c.Id == step.CueId) is { } record)
                _show.RemoveCue(record);
            Wrote($"cue {step.CueNumberText} removed");
            Check("cue steps removed or put in by Kloarte (cue stack record)");
        }
        var insert = new List<(int CueId, float Number, string Text, string Label)>();
        foreach (var cue in _design.CueOrder)
        {
            string text = Clean(cue.Text);
            var step = cue.Id is { } id ? stack.Steps.FirstOrDefault(s => s.CueId == id) : null;
            if (step == null)
            {
                var added = NewCue(cue, otherIds);
                cue.Id = added.Id;
                insert.Add((added.Id, cue.Number, text, $"cue {cue.Cue} added"));
                continue;
            }
            var record = _show.Cues.FirstOrDefault(c => c.Id == step.CueId);
            if (record != null)
                UpdateCue(record, cue, step.Text);
            else if (_design.ChannelsOf(cue).Count > 0)
                Warnings.Add($"cue {cue.Cue}: MagicQ has no cue record for its step: its values are not exported");
            if (!CueNumbers.Same(step.CueNumber, cue.Number))
            {
                stack.RemoveStep(step.CueId);
                insert.Add((step.CueId, cue.Number, text, $"cue {step.CueNumberText} renumbered {cue.Cue}"));
            }
            else if ((cue.Text ?? "") != step.Text && step.Text != text)
            {
                stack.SetStepText(step.CueId, text);
                Wrote($"cue {cue.Cue}: text \"{text}\"");
            }
        }
        foreach (var (cueId, cueNumber, text, label) in insert.OrderBy(i => i.Number))
        {
            try
            {
                stack.InsertStep(cueId, cueNumber, text);
                Wrote(label);
                Check("cue steps removed or put in by Kloarte (cue stack record)");
            }
            catch (ArgumentException e)
            {
                Warnings.Add($"{label}: {e.Message} (not in the stack)");
            }
        }
        var delayed = _design.CueOrder.Where(c => (c.Delay ?? 0) > 0).Select(c => c.Cue).ToList();
        if (delayed.Count > 0)
            Warnings.Add($"delays are Kloarte's only, not exported: cue {List(delayed)}");
    }

    /// <summary>Sets what differs of a cue record: channels, In / Out times, name.</summary>
    private void UpdateCue(MagicQShow.Cue record, DesignCue cue, string stepText)
    {
        var want = _design.ChannelsOf(cue).ToDictionary(c => c.Address, Raw);
        var now = record.Channels.ToDictionary(c => c.Address, c => c.Value);
        int changed = 0;
        foreach (int address in now.Keys.Where(a => !want.ContainsKey(a)).ToList())
        {
            record.RemoveChannel(address);
            changed++;
        }
        foreach (var (address, raw) in want)
            if (!now.TryGetValue(address, out int current) || Changes(address, current, raw))
            {
                record.SetChannel(address, raw);
                changed++;
            }
        if (changed > 0)
        {
            Wrote($"cue {cue.Cue}: {changed} channel{(changed == 1 ? "" : "s")}");
            Check("cue channels written by Kloarte (value pairs in address order)");
        }
        int fadeIn = MagicQTimes.Units(cue.FadeIn ?? 0), fadeOut = MagicQTimes.Units(cue.FadeOut ?? 0);
        if (record.Record.GetHex(6) != fadeIn || record.Record.GetHex(7) != fadeOut)
        {
            record.SetFade(record.Record.GetHex(6) != fadeIn ? (float)(cue.FadeIn ?? 0) : null,
                           record.Record.GetHex(7) != fadeOut ? (float)(cue.FadeOut ?? 0) : null);
            Wrote($"cue {cue.Cue}: fade {DesignShow.Seconds(cue.FadeIn)} / {DesignShow.Seconds(cue.FadeOut)} s");
            Check("cue times written by Kloarte (In / Out; the record's millisecond copies are not changed)");
        }
        // The name follows the text (as imported: no name of its own) only when the text changed.
        string name = cue.Name != null
            ? record.Name != cue.Name ? Clean(cue.Name) : null
            : record.Name.Length > 0 && record.Name == stepText && (cue.Text ?? "") != stepText ? Clean(cue.Text) : null;
        if (name != null && record.Name != name)
        {
            record.Name = name;
            Wrote($"cue {cue.Cue}: name \"{name}\"");
        }
    }

    /// <summary>
    /// True when a cue value must be written: it gives another DMX value, or it refers to a palette the value now doesn't.
    /// A hard value equal to what a palette reference gives leaves the reference.
    /// </summary>
    private bool Changes(int address, int current, int raw)
    {
        var (nowPalette, nowValue) = Normal(address, current);
        var (wantPalette, wantValue) = Normal(address, raw);
        return nowValue != wantValue || wantPalette != 0 && wantPalette != nowPalette;
    }

    /// <summary>A cue value as MagicQ plays it: the palette (when it holds the address) and the DMX value.</summary>
    private (int Palette, int Value) Normal(int address, int raw)
    {
        int id = raw >> 8;
        bool held = id != 0 && _show.FindPalette(id) is { } p && p.TryGetValue(address, out _, out _);
        return (held ? id : 0, _show.Resolve(address, raw));
    }

    /// <summary>A new cue record (copied from the simplest cue, the next free id) with the design cue's channels and times.</summary>
    private MagicQShow.Cue NewCue(DesignCue cue, HashSet<int> otherIds)
    {
        var record = _show.AddRecordFrom(SimplestRecord(r => r.Type == "S" && r.FieldCount > 3));
        var added = new MagicQShow.Cue(record);
        foreach (var (address, _) in added.Channels.ToList())
            added.RemoveChannel(address);
        int id = Math.Max(_show.Cues.Select(c => c.Id).DefaultIfEmpty(0).Max(),
                          _show.CueStacks.SelectMany(s => s.Steps).Select(s => s.CueId).DefaultIfEmpty(0).Max()) + 1;
        record[1] = id.ToString("x4", CultureInfo.InvariantCulture);
        added.Name = Clean(cue.Name ?? cue.Text);
        foreach (var channel in _design.ChannelsOf(cue))
            added.SetChannel(channel.Address, Raw(channel));
        added.SetFade((float)(cue.FadeIn ?? 0), (float)(cue.FadeOut ?? 0));
        _show.Cues.Add(added);
        Check("new cues (cue records copied from the simplest cue; times: In / Out only)");
        return added;
    }

    /// <summary>A new cue stack (no steps), copied from the simplest stack record.</summary>
    private MagicQShow.CueStack NewStack(int number)
    {
        var record = _show.AddRecordFrom(SimplestRecord(r => r.Type == "C" && r.FieldCount > 4));
        record.SetHex(1, number);
        var stack = new MagicQShow.CueStack(record);
        foreach (var step in stack.Steps.ToList())
            stack.RemoveStep(step.CueId);
        stack.Name = Clean(_design.CueList.Name ?? "Cues");
        _show.CueStacks.Add(stack);
        Wrote($"cue stack {number} added");
        Check("a new cue stack (copied from another stack record)");
        return stack;
    }
}
