using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

public static class KeywordReminder
{
    static readonly Dictionary<string, string> Reminders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "alliance", "When this Digimon attacks, by suspending 1 of your other Digimon, this Digimon adds the suspended Digimon's DP and gains <Security Attack +1> for the attack." },
        { "armor purge", "When this Digimon would be deleted, you may trash the top card of this Digimon to prevent that deletion." },
        { "arts digivolve", "Arts Digivolve allows you to digivolve a Digimon in your battle area or breeding area into a DUAL Card with [Arts Digivolve] after using the Option Card effect." },
        { "ascension", "When this Digimon is deleted, you may place this card as the top security card." },
        { "barrier", "When this Digimon would be deleted in battle, by trashing the top card of your security stack, prevent that deletion." },
        { "blast digivolve", "Your Digimon may digivolve into this card without paying the cost." },
        { "blast dna digivolve", "One of your specified Digimon and 1 of the specified card in the hand may DNA Digivolve into this card." },
        { "blitz", "This Digimon can attack when your opponent has 1 or more memory." },
        { "blocker", "When an opponent's Digimon attacks, you may suspend this Digimon to force the opponent to attack it instead." },
        { "collision", "During this Digimon's attack, all of your opponent's Digimon gain <Blocker>, and your opponent blocks if possible." },
        { "decode", "When this Digimon would leave the battle area other than in battle, you may play 1 specified Digimon card from its digivolution cards without paying the cost." },
        { "decoy", "When one of your other Digimon of the named color would be deleted by an opponent's effect, you may delete this Digimon to prevent that deletion." },
        { "delay", "Trash this card in your battle area to activate the effect below. You can't activate this effect the turn this card enters play." },
        { "de-digivolve", "Trash that many cards from the top of one of your opponent's Digimon. You can't trash past level 3 cards." },
        { "detach", "When this Digimon would leave the battle area other than by your effects, by trashing 1 of its matching link cards, it doesn't leave." },
        { "digi-burst", "Trash that many of this Digimon's digivolution cards to activate the effect below." },
        { "digisorption", "When one of your Digimon digivolves into this card from your hand, you may suspend 1 of your Digimon to reduce the memory cost by that amount." },
        { "draw", "Draw that many cards from your deck." },
        { "engage", "At the end of your turn, this Digimon may attack." },
        { "evade", "When this Digimon would be deleted, you may suspend it to prevent that deletion." },
        { "execute", "At the end of your turn, this Digimon may attack. At the end of that attack, delete this Digimon. Your opponent's unsuspended Digimon can also be attacked with this effect." },
        { "fortitude", "When this Digimon with digivolution cards is deleted, play this card without paying the cost." },
        { "fragment", "When this Digimon would be deleted, by trashing the stated number of its digivolution cards, it isn't deleted." },
        { "guard", "When any of your other Digimon would leave the battle area by your opponent's effects, by deleting this Digimon, they don't leave." },
        { "iceclad", "Digimon with Iceclad compares its number of digivolution cards instead of DP in battles other than with security Digimon." },
        { "jamming", "This Digimon can't be deleted in battles against Security Digimon." },
        { "link", "Plug this card from the hand or battle area sideways into the specified Digimon in the battle area." },
        { "material save", "When this Digimon is deleted, you may place that many cards from its DigiXros conditions under 1 of your Tamers." },
        { "mind link", "Place this Tamer as that Digimon's bottom digivolution card if there are no Tamer cards in its digivolution cards." },
        { "overclock", "At the end of your turn, by deleting 1 of your Tokens or other Digimon with the named trait, this Digimon attacks a player without suspending." },
        { "partition", "When this Digimon with 1 of each specified card in its digivolution cards would leave the battle area other than by one of your effects or in battle, you may play 1 of each card without paying their costs." },
        { "piercing", "When this Digimon deletes your opponent's Digimon in battle while attacking, it checks security before the attack ends." },
        { "pierce", "When this Digimon deletes your opponent's Digimon in battle while attacking, it checks security before the attack ends." },
        { "progress", "While attacking, your opponent's effects don't affect this Digimon." },
        { "raid", "When this Digimon attacks, you may switch the target of attack to 1 of your opponent's unsuspended Digimon with the highest DP." },
        { "reboot", "Unsuspend this Digimon during your opponent's unsuspend phase." },
        { "recovery", "Place the top card of your deck on top of your security stack." },
        { "retaliation", "When this Digimon is deleted after losing a battle, delete the Digimon it was battling." },
        { "rush", "This Digimon can attack the turn it comes into play." },
        { "save", "You may place this card under one of your Tamers." },
        { "scapegoat", "When this Digimon would be deleted other than by your effects, by deleting 1 of your other Digimon, it isn't deleted." },
        { "security attack", "This Digimon checks that many additional or fewer security cards." },
        { "succession", "Your Digimon with Succession gains all effects other than Succession on its topmost specified digivolution card." },
        { "training", "In the main phase, by suspending this Digimon, place your deck's top card face down as this Digimon's bottom digivolution card. This effect can also activate in the breeding area." },
        { "vortex", "At the end of your turn, this Digimon may attack an opponent's Digimon. With this effect, it can attack the turn it was played." },
    };

    static readonly string[] StandaloneNames =
    {
        "Blast DNA Digivolve",
        "Blast Digivolve",
        "Arts Digivolve",
        "Material Save",
        "Armor Purge",
        "Security Attack",
        "Security A.",
        "Mind Link",
        "Digi-Burst",
        "Digisorption",
        "De-Digivolve",
        "Retaliation",
        "Collision",
        "Partition",
        "Ascension",
        "Fortitude",
        "Alliance",
        "Training",
        "Progress",
        "Execute",
        "Overclock",
        "Fragment",
        "Scapegoat",
        "Succession",
        "Iceclad",
        "Piercing",
        "Pierce",
        "Barrier",
        "Blocker",
        "Reboot",
        "Evade",
        "Jamming",
        "Decode",
        "Detach",
        "Engage",
        "Vortex",
        "Guard",
        "Blitz",
        "Raid",
        "Rush",
        "Save",
        "Link",
        "Draw",
        "Recovery",
        "Delay",
        "Decoy",
    };

    static readonly Regex TrailingValueRegex = new Regex(@"\s*[+-]?\s*\d+\s*$", RegexOptions.Compiled);
    static readonly Regex TrailingSignRegex = new Regex(@"\s+[+-]\s*$", RegexOptions.Compiled);

    public static bool TryGetReminder(string keyword, out string reminder)
    {
        reminder = null;
        string key = NormalizeKey(keyword);
        return !string.IsNullOrEmpty(key) && Reminders.TryGetValue(key, out reminder);
    }

    public static string WrapKeywords(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        text = DataBase.ReplaceToASCII(text);
        text = WrapAngleKeywords(text);
        text = WrapBracketLink(text);
        return WrapStandaloneKeywords(text);
    }

    public static string NormalizeKey(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return "";
        }

        raw = DataBase.ReplaceToASCII(raw).Trim();
        raw = raw.Replace("&lt;", "<").Replace("&gt;", ">");
        raw = raw.Trim('<', '>').Trim();
        raw = raw.Trim('[', ']').Trim();

        int paren = raw.IndexOf('(');
        if (paren > 0)
        {
            raw = raw.Substring(0, paren).Trim();
        }

        int bracket = raw.IndexOf('[');
        if (bracket > 0)
        {
            raw = raw.Substring(0, bracket).Trim();
        }

        int innerAngle = raw.IndexOf('<');
        if (innerAngle > 0)
        {
            raw = raw.Substring(0, innerAngle).Trim();
        }

        raw = TrailingValueRegex.Replace(raw, "").Trim();
        raw = TrailingSignRegex.Replace(raw, "").Trim();
        raw = raw.ToLowerInvariant();

        if (raw.StartsWith("security a"))
        {
            return "security attack";
        }

        if (raw == "pierce")
        {
            return "piercing";
        }

        return raw;
    }

    static string WrapAngleKeywords(string text)
    {
        StringBuilder builder = new StringBuilder(text.Length + 32);
        int index = 0;

        while (index < text.Length)
        {
            if (text[index] == '<' && !IsExistingMarkup(text, index))
            {
                int end = FindMatchingCloseAngle(text, index);
                if (end > index)
                {
                    string token = text.Substring(index, end - index + 1);
                    if (TryGetReminder(token, out _))
                    {
                        builder.Append(MakeLink(NormalizeKey(token), token));
                    }
                    else
                    {
                        builder.Append(WrapLiteral(token));
                    }

                    index = end + 1;
                    continue;
                }
            }

            builder.Append(text[index]);
            index++;
        }

        return builder.ToString();
    }

    static string WrapBracketLink(string text)
    {
        const string token = "[Link]";
        int index = 0;
        StringBuilder builder = new StringBuilder(text.Length + 16);

        while (index < text.Length)
        {
            if (TrySkipMarkup(text, index, builder, out int skippedIndex))
            {
                index = skippedIndex;
                continue;
            }

            if (StartsWithIgnoreCase(text, index, token) && IsBoundary(text, index, token.Length))
            {
                builder.Append(MakeLink("link", token));
                index += token.Length;
                continue;
            }

            builder.Append(text[index]);
            index++;
        }

        return builder.ToString();
    }

    static string WrapStandaloneKeywords(string text)
    {
        StringBuilder builder = new StringBuilder(text.Length + 32);
        int index = 0;

        while (index < text.Length)
        {
            if (TrySkipMarkup(text, index, builder, out int skippedIndex))
            {
                index = skippedIndex;
                continue;
            }

            bool matched = false;
            for (int nameIndex = 0; nameIndex < StandaloneNames.Length; nameIndex++)
            {
                string name = StandaloneNames[nameIndex];
                if (index + name.Length > text.Length || !StartsWithIgnoreCase(text, index, name) || !IsBoundary(text, index, name.Length))
                {
                    continue;
                }

                string key = NormalizeKey(name);
                if (!TryGetReminder(key, out _))
                {
                    continue;
                }

                builder.Append(MakeLink(key, text.Substring(index, name.Length)));
                index += name.Length;
                matched = true;
                break;
            }

            if (!matched)
            {
                builder.Append(text[index]);
                index++;
            }
        }

        return builder.ToString();
    }

    static string MakeLink(string key, string display)
    {
        return $"<link={key}><u>{WrapLiteral(display)}</u></link>";
    }

    static string WrapLiteral(string value)
    {
        return $"<noparse>{value}</noparse>";
    }

    static bool TrySkipMarkup(string text, int index, StringBuilder builder, out int newIndex)
    {
        if (StartsWithIgnoreCase(text, index, "<link="))
        {
            int close = IndexOfIgnoreCase(text, "</link>", index);
            if (close >= 0)
            {
                builder.Append(text, index, close + 7 - index);
                newIndex = close + 7;
                return true;
            }
        }

        if (StartsWithIgnoreCase(text, index, "<noparse>"))
        {
            int close = IndexOfIgnoreCase(text, "</noparse>", index);
            if (close >= 0)
            {
                builder.Append(text, index, close + 10 - index);
                newIndex = close + 10;
                return true;
            }
        }

        newIndex = index;
        return false;
    }

    static bool IsExistingMarkup(string text, int index)
    {
        return StartsWithIgnoreCase(text, index, "<link=")
            || StartsWithIgnoreCase(text, index, "<noparse")
            || StartsWithIgnoreCase(text, index, "</")
            || StartsWithIgnoreCase(text, index, "<color")
            || StartsWithIgnoreCase(text, index, "<b>")
            || StartsWithIgnoreCase(text, index, "<i>")
            || StartsWithIgnoreCase(text, index, "<u>");
    }

    static int FindMatchingCloseAngle(string text, int start)
    {
        int depth = 0;
        for (int index = start; index < text.Length; index++)
        {
            if (text[index] == '<')
            {
                depth++;
            }
            else if (text[index] == '>')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    static bool IsBoundary(string text, int start, int length)
    {
        if (start > 0 && char.IsLetterOrDigit(text[start - 1]))
        {
            return false;
        }

        int after = start + length;
        return after >= text.Length || !char.IsLetterOrDigit(text[after]);
    }

    static bool StartsWithIgnoreCase(string text, int index, string value)
    {
        if (index + value.Length > text.Length)
        {
            return false;
        }

        return string.Compare(text, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }

    static int IndexOfIgnoreCase(string text, string value, int startIndex)
    {
        return text.IndexOf(value, startIndex, StringComparison.OrdinalIgnoreCase);
    }
}
