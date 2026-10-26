using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DCGO.DSTS.EditorTools
{
    /// <summary>
    /// Game animation code -> friendly name. Table taken from the nuffle.me DSTS viewer
    /// (MIT licensed), with field variants renamed so every name is unique.
    /// Clip names in the GLB look like "chr629_bd02": the part before the first '_' is the
    /// character id, the rest is the code.
    /// </summary>
    public static class DSTSAnimNames
    {
        static readonly Dictionary<string, string> Exact = new Dictionary<string, string>
        {
            { "bn01", "Idle" },              { "bn02", "Idle (Low HP)" },        { "bn90", "Sit (Special)" },
            { "ba01", "Attack 01" },         { "ba02", "Attack 02" },            { "ba03", "Attack 03" },
            { "bd01", "Damage (Hit)" },      { "bd02", "Damage (Knock Down)" },  { "bd03", "Damage (Get Up)" },
            { "bg01", "Guard (Block)" },     { "bg02", "Guard (Dodge)" },
            { "br01", "Run" },               { "bf01", "Field Action" },         { "bv01", "Battle Victory" },
            { "bs01", "Special 01" },        { "bs02", "Special 02" },           { "bs03", "Special 03" },
            { "bs04", "Special 04" },        { "bs08", "Special 08" },
            { "bb01", "Boss Sequence 01" },  { "bb02", "Boss Sequence 02" },     { "bb03", "Boss Sequence 03" },
            { "bb04", "Boss Sequence 04" },  { "bb05", "Boss Sequence 05" },     { "bb06", "Boss Sequence 06" },
            { "fe01", "Eat" },               { "fe02", "Victory (Field)" },      { "fe03", "Yawn" },
            { "fe04", "Fight (Field)" },
            { "fq01", "Yes" },               { "fq02", "No" },
            { "f000", "Rest 01" },           { "f001", "Rest 02" },
            { "fw01_01", "Walk" },           { "fn01_01", "Idle (Field)" },      { "fr01_01", "Run (Field)" },
            { "fs01_01", "Sit" },
            { "ft01_01", "Turn (Right)" },   { "ft01_02", "Turn (Left)" },
            { "ft02_01", "Turn (Sharp Right)" }, { "ft02_02", "Turn (Sharp Left)" },
            { "ft03_01", "Turn (Neutral)" },
        };

        static readonly HashSet<string> Looping = new HashSet<string>
        {
            "bn01", "bn02", "bn90", "br01", "f000", "f001", "fw01_01", "fn01_01", "fr01_01", "fs01_01",
        };

        static readonly Regex BindPose  = new Regex(@"^(chr|npc)\d{3}a?$");
        static readonly Regex MountIdle = new Regex(@"^r\d{3}_fn01$");
        static readonly Regex MountRun  = new Regex(@"^r\d{3}_fw01$");

        /// <summary>"chr629_bd02" -> "bd02". Names without '_' are returned unchanged.</summary>
        public static string CodeOf(string clipName)
        {
            int u = clipName.IndexOf('_');
            return u < 0 ? clipName : clipName.Substring(u + 1);
        }

        public static bool IsBindPose(string clipName) => BindPose.IsMatch(clipName);

        public static string Friendly(string code)
        {
            if (Exact.TryGetValue(code, out var n)) return n;
            if (MountIdle.IsMatch(code)) return "Mount Idle";
            if (MountRun.IsMatch(code)) return "Mount Run";
            return code; // unknown: keep the game code
        }

        public static bool IsLoop(string code)
            => Looping.Contains(code) || MountIdle.IsMatch(code) || MountRun.IsMatch(code);

        /// <summary>Preferred default states, in order.</summary>
        public static readonly string[] DefaultStatePreference = { "Idle", "Idle (Field)", "Idle (Low HP)", "Mount Idle" };
    }
}
