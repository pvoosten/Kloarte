using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Kloarte.Model;

namespace Kloarte.MagicQ;

public partial class MagicQShow
{
    /// <summary>
    /// C: cue stack. Header: 1 number, 2 name, 4 step count; a 17-field line; then 7 fields per step, one line each:
    /// next step, previous step (a circular list in record order), cue id (-&gt; S record), ?, flags (00006000;
    /// 0000c000 = mark cue), cue number (e.g. 8.11), text (max 15 characters). After the steps: a notes flag
    /// (00000001: followed by two strings per step, the second a step note), a 5-field line and one 8-digit field per
    /// step. In all 727 stacks of the MagicQ show folder the steps are in cue-number order and linked in record order;
    /// <see cref="InsertStep"/> keeps that.
    /// </summary>
    public sealed class CueStack : RecordView, ICueStack
    {
        private const int FirstStep = 23, StepFields = 7;
        /// <summary>Creates the view on a C record.</summary>
        public CueStack(ShwRecord r) : base(r) { }
        /// <summary>Cue stack number.</summary>
        public int Number => Record.GetHex(1);
        /// <summary>Cue stack name.</summary>
        public string Name { get => Record.GetString(2); set => Record.SetString(2, value); }

        /// <inheritdoc />
        IReadOnlyList<ICueStep> ICueStack.Steps => Steps;

        /// <summary>The steps, sorted by cue number.</summary>
        public IReadOnlyList<Step> Steps
        {
            get
            {
                var steps = new List<Step>();
                int count = Record.GetHex(4);
                for (int i = 0; i < count && FirstStep + (i + 1) * StepFields <= Record.FieldCount; i++)
                {
                    int f = FirstStep + i * StepFields;
                    steps.Add(new Step(Record.GetHex(f), Record.GetHex(f + 2), Record.GetFloat(f + 5), Record.GetString(f + 6)));
                }
                return steps.OrderBy(s => s.CueNumber).ToList();
            }
        }

        /// <summary>Longest step text MagicQ keeps.</summary>
        private const int MaxText = 15;

        /// <summary>
        /// Inserts a step that plays cue `cueId` as cue `number`, in cue-number order: its line among the steps, its two
        /// note strings (when the stack has notes) and its per-step field, the count, and the next / previous links of
        /// every step. Throws when the number is already used.
        /// </summary>
        public Step InsertStep(int cueId, float number, string text)
        {
            var steps = Steps;
            if (HasNumber(number))
                throw new ArgumentException($"cue {number.ToString("0.###", CultureInfo.InvariantCulture)} already exists in this stack");
            int n = Record.GetHex(4);
            int k = steps.Count(st => st.CueNumber < number);   // record position = position in cue order
            int flag = FirstStep + StepFields * n;
            int notes = Record.GetHex(flag) != 0 ? 2 * n : 0;
            int five = flag + 1 + notes;
            int perStep = five + 5;

            // Back to front, so the earlier field numbers stay valid.
            Record.InsertLineAfter(k > 0 ? perStep + k - 1 : five + 4, new[] { "00000000" });
            if (notes > 0)
            {
                if (k > 0)
                    Record.InsertLineAfter(flag + 2 * k, new[] { "\"\"", "\"\"" });
                else
                    Record.InsertFieldsAfter(flag, new[] { "\"\"", "\"\"" });
            }
            if (text.Length > MaxText)
                text = text[..MaxText];
            Record.InsertLineAfter(k > 0 ? FirstStep + StepFields * k - 1 : FirstStep - 1, new[]
            {
                "0000", "0000", cueId.ToString("x4", CultureInfo.InvariantCulture), "0000", "00006000",
                number.ToString("F6", CultureInfo.InvariantCulture), "\"" + text.Replace("\"", "'") + "\"",
            });
            Record.SetHex(4, n + 1);
            for (int i = 0; i <= n; i++)
            {
                int f = FirstStep + StepFields * i;
                Record.SetHex(f, (i + 1) % (n + 1));
                Record.SetHex(f + 1, (i + n) % (n + 1));
            }
            return Steps.First(st => st.CueId == cueId && MathF.Abs(st.CueNumber - number) < 0.0005f);
        }

        /// <summary>
        /// Removes the step that plays cue `cueId` (the inverse of <see cref="InsertStep"/>): its line, its two note
        /// strings (when the stack has notes) and its per-step field, the count, and the links of the other steps. The
        /// cue record stays. Throws when the stack has no such step.
        /// </summary>
        public void RemoveStep(int cueId)
        {
            int n = Record.GetHex(4);
            int k = -1;
            for (int i = 0; i < n; i++)
                if (Record.GetHex(FirstStep + i * StepFields + 2) == cueId)
                    k = i;
            if (k < 0)
                throw new ArgumentException($"no step plays cue id {cueId}");
            int flag = FirstStep + StepFields * n;
            int notes = Record.GetHex(flag) != 0 ? 2 * n : 0;
            int five = flag + 1 + notes;
            int perStep = five + 5;
            // Back to front, so the earlier field numbers stay valid.
            Record.RemoveFieldsOrLine(perStep + k, 1);
            if (notes > 0)
                Record.RemoveFieldsOrLine(flag + 1 + 2 * k, 2);
            Record.RemoveFieldsOrLine(FirstStep + StepFields * k, StepFields);
            Record.SetHex(4, n - 1);
            for (int i = 0; i < n - 1; i++)
            {
                int f = FirstStep + StepFields * i;
                Record.SetHex(f, (i + 1) % (n - 1));
                Record.SetHex(f + 1, (i + n - 2) % (n - 1));
            }
        }

        /// <summary>The first field of the step that plays cue `cueId`; throws when the stack has no such step.</summary>
        private int StepField(int cueId)
        {
            int count = Record.GetHex(4);
            for (int i = 0; i < count && FirstStep + (i + 1) * StepFields <= Record.FieldCount; i++)
                if (Record.GetHex(FirstStep + i * StepFields + 2) == cueId)
                    return FirstStep + i * StepFields;
            throw new ArgumentException($"no step plays cue id {cueId}");
        }

        /// <summary>Sets the text of the step that plays a cue (cut to 15 characters; double quotes become single).</summary>
        public void SetStepText(int cueId, string text)
        {
            text = (text ?? "").Replace("\"", "'");
            Record.SetString(StepField(cueId) + 6, text.Length > MaxText ? text[..MaxText] : text);
        }

        /// <summary>
        /// Gives the step that plays a cue another cue number. The steps stay in cue-number order (the stack's lists and
        /// links depend on it): the number must lie between those of the neighbouring steps. Throws when it doesn't.
        /// </summary>
        public void SetStepNumber(int cueId, float number)
        {
            int field = StepField(cueId);
            var steps = Steps.ToList();
            int at = steps.FindIndex(s => s.CueId == cueId);
            if (steps.Where((s, i) => i != at).Any(s => MathF.Abs(s.CueNumber - number) < 0.0005f))
                throw new ArgumentException($"cue {number.ToString("0.###", CultureInfo.InvariantCulture)} already exists in this stack");
            if (at > 0 && number <= steps[at - 1].CueNumber || at + 1 < steps.Count && number >= steps[at + 1].CueNumber)
                throw new ArgumentException("the new number would change the order of the cues");
            Record[field + 5] = number.ToString("F6", CultureInfo.InvariantCulture);
        }

        /// <summary>True when a step of this stack has cue number `number`.</summary>
        public bool HasNumber(float number) => Steps.Any(st => MathF.Abs(st.CueNumber - number) < 0.0005f);

        /// <summary>
        /// A free cue number right after `current` (null: before the first cue) and before `next` (null: the last
        /// cue), with as few decimals as possible, like inserting in MagicQ: 1 -&gt; 2 when free, 1 and 2 -&gt; 1.1,
        /// 1.1 and 1.2 -&gt; 1.11. Null when there is no room with 3 decimals.
        /// </summary>
        public static float? NumberBetween(float? current, float? next) => CueNumbers.Between(current, next);

        /// <summary>A step of a cue stack.</summary>
        /// <param name="Index">Step index in the record.</param>
        /// <param name="CueId">Cue (S record) id.</param>
        /// <param name="CueNumber">Cue number.</param>
        /// <param name="Text">Step text.</param>
        public sealed record Step(int Index, int CueId, float CueNumber, string Text) : ICueStep
        {
            /// <summary>Cue number as MagicQ shows it, e.g. "12.1".</summary>
            public string CueNumberText => CueNumber.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
