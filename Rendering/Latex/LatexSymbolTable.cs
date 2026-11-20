using System;
using System.Collections.Generic;

namespace NoteDraw.Rendering.Latex
{
    /// <summary>
    /// Lookup tables for LaTeX commands, Greek letters, operators, functions,
    /// blackboard bold glyphs, and spacing commands.
    /// Maintainers: Add new symbol mappings directly to the dictionaries below.
    /// </summary>
    public static class LatexSymbolTable
    {
        /// <summary>
        /// Maps LaTeX command names (without leading backslash) to rendered Unicode strings.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Symbols =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Lowercase Greek letters
                ["alpha"] = "\u03B1",
                ["beta"] = "\u03B2",
                ["gamma"] = "\u03B3",
                ["delta"] = "\u03B4",
                ["epsilon"] = "\u03B5",
                ["varepsilon"] = "\u03B5",
                ["zeta"] = "\u03B6",
                ["eta"] = "\u03B7",
                ["theta"] = "\u03B8",
                ["vartheta"] = "\u03D1",
                ["iota"] = "\u03B9",
                ["kappa"] = "\u03BA",
                ["lambda"] = "\u03BB",
                ["mu"] = "\u03BC",
                ["nu"] = "\u03BD",
                ["xi"] = "\u03BE",
                ["pi"] = "\u03C0",
                ["varpi"] = "\u03D6",
                ["rho"] = "\u03C1",
                ["varrho"] = "\u03F1",
                ["sigma"] = "\u03C3",
                ["varsigma"] = "\u03C2",
                ["tau"] = "\u03C4",
                ["upsilon"] = "\u03C5",
                ["phi"] = "\u03D5",
                ["varphi"] = "\u03C6",
                ["chi"] = "\u03C7",
                ["psi"] = "\u03C8",
                ["omega"] = "\u03C9",

                // Uppercase Greek letters
                ["Gamma"] = "\u0393",
                ["Delta"] = "\u0394",
                ["Theta"] = "\u0398",
                ["Lambda"] = "\u039B",
                ["Xi"] = "\u039E",
                ["Pi"] = "\u03A0",
                ["Sigma"] = "\u03A3",
                ["Upsilon"] = "\u03A5",
                ["Phi"] = "\u03A6",
                ["Psi"] = "\u03A8",
                ["Omega"] = "\u03A9",

                // Binary operators
                ["pm"] = "\u00B1",
                ["mp"] = "\u2213",
                ["times"] = "\u00D7",
                ["div"] = "\u00F7",
                ["cdot"] = "\u22C5",
                ["ast"] = "\u2217",
                ["star"] = "\u22C6",
                ["circ"] = "\u2218",
                ["bullet"] = "\u2022",
                ["oplus"] = "\u2295",
                ["ominus"] = "\u2296",
                ["otimes"] = "\u2297",
                ["odot"] = "\u2299",
                ["wedge"] = "\u2227",
                ["vee"] = "\u2228",
                ["setminus"] = "\u2216",

                // Relations & comparisons
                ["leq"] = "\u2264",
                ["le"] = "\u2264",
                ["geq"] = "\u2265",
                ["ge"] = "\u2265",
                ["neq"] = "\u2260",
                ["ne"] = "\u2260",
                ["approx"] = "\u2248",
                ["equiv"] = "\u2261",
                ["sim"] = "\u223C",
                ["simeq"] = "\u2243",
                ["cong"] = "\u2245",
                ["propto"] = "\u221D",
                ["ll"] = "\u226A",
                ["gg"] = "\u226B",
                ["subset"] = "\u2282",
                ["subseteq"] = "\u2286",
                ["supset"] = "\u2283",
                ["supseteq"] = "\u2287",
                ["in"] = "\u2208",
                ["notin"] = "\u2209",
                ["ni"] = "\u220B",
                ["cup"] = "\u222A",
                ["cap"] = "\u2229",
                ["perp"] = "\u22A5",
                ["parallel"] = "\u2225",
                ["mid"] = "\u2223",

                // Logic, calculus & arrows
                ["forall"] = "\u2200",
                ["exists"] = "\u2203",
                ["nexists"] = "\u2204",
                ["neg"] = "\u00AC",
                ["lnot"] = "\u00AC",
                ["land"] = "\u2227",
                ["lor"] = "\u2228",
                ["implies"] = "\u27F9",
                ["iff"] = "\u27FA",
                ["to"] = "\u2192",
                ["rightarrow"] = "\u2192",
                ["leftarrow"] = "\u2190",
                ["Rightarrow"] = "\u21D2",
                ["Leftarrow"] = "\u21D0",
                ["leftrightarrow"] = "\u2194",
                ["Leftrightarrow"] = "\u21D4",
                ["mapsto"] = "\u21A6",
                ["uparrow"] = "\u2191",
                ["downarrow"] = "\u2193",
                ["emptyset"] = "\u2205",
                ["varnothing"] = "\u2205",
                ["infty"] = "\u221E",
                ["partial"] = "\u2202",
                ["nabla"] = "\u2207",
                ["hbar"] = "\u210F",
                ["ell"] = "\u2113",
                ["Re"] = "\u211C",
                ["Im"] = "\u2111",
                ["aleph"] = "\u2135",
                ["degree"] = "\u00B0",
                ["prime"] = "\u2032",
                ["angle"] = "\u2220",
                ["therefore"] = "\u2234",
                ["because"] = "\u2235",
                ["dots"] = "\u2026",
                ["ldots"] = "\u2026",
                ["cdots"] = "\u22EF",
                ["vdots"] = "\u22EE",
                ["ddots"] = "\u22F1",

                // Delimiters & punctuation aliases
                ["{"] = "{",
                ["}"] = "}",
                ["%"] = "%",
                ["&"] = "&",
                ["$"] = "$",
                ["#"] = "#",
                ["_"] = "_",
                ["langle"] = "\u27E8",
                ["rangle"] = "\u27E9",
                ["lfloor"] = "\u230A",
                ["rfloor"] = "\u230B",
                ["lceil"] = "\u2308",
                ["rceil"] = "\u2309",
                ["lvert"] = "|",
                ["rvert"] = "|",
                ["vert"] = "|",
                ["lVert"] = "\u2016",
                ["rVert"] = "\u2016",
                ["Vert"] = "\u2016",
                ["|"] = "\u2016"
            };

        /// <summary>
        /// Big math operators and whether their sub/superscripts stack above/below by default in display mode.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, (string Glyph, bool StackLimits)> BigOperators =
            new Dictionary<string, (string Glyph, bool StackLimits)>(StringComparer.Ordinal)
            {
                ["int"] = ("\u222B", false),
                ["iint"] = ("\u222C", false),
                ["iiint"] = ("\u222D", false),
                ["oint"] = ("\u222E", false),
                ["sum"] = ("\u2211", true),
                ["prod"] = ("\u220F", true),
                ["coprod"] = ("\u2210", true),
                ["bigcup"] = ("\u22C3", true),
                ["bigcap"] = ("\u22C2", true),
                ["bigoplus"] = ("\u2A01", true),
                ["bigotimes"] = ("\u2A02", true),
                ["lim"] = ("lim", true),
                ["limsup"] = ("lim sup", true),
                ["liminf"] = ("lim inf", true),
                ["max"] = ("max", true),
                ["min"] = ("min", true),
                ["sup"] = ("sup", true),
                ["inf"] = ("inf", true)
            };

        /// <summary>
        /// Standard named mathematical functions rendered upright.
        /// </summary>
        public static readonly IReadOnlySet<string> NamedFunctions =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "sin", "cos", "tan", "cot", "sec", "csc",
                "arcsin", "arccos", "arctan",
                "sinh", "cosh", "tanh", "coth",
                "log", "ln", "lg", "exp",
                "det", "dim", "ker", "gcd", "deg", "arg", "mod", "bmod", "Pr", "hom"
            };

        /// <summary>
        /// Maps single-letter or multi-letter identifiers inside \mathbb{...} to Unicode blackboard bold.
        /// </summary>
        public static readonly IReadOnlyDictionary<char, string> BlackboardBold =
            new Dictionary<char, string>
            {
                ['C'] = "\u2102",
                ['H'] = "\u210D",
                ['N'] = "\u2115",
                ['P'] = "\u2119",
                ['Q'] = "\u211A",
                ['R'] = "\u211D",
                ['Z'] = "\u2124",
                ['A'] = "\U0001D538",
                ['B'] = "\U0001D539",
                ['D'] = "\U0001D53B",
                ['E'] = "\U0001D53C",
                ['F'] = "\U0001D53D",
                ['G'] = "\U0001D53E",
                ['I'] = "\U0001D540",
                ['J'] = "\U0001D541",
                ['K'] = "\U0001D542",
                ['L'] = "\U0001D543",
                ['M'] = "\U0001D544",
                ['O'] = "\U0001D546",
                ['S'] = "\U0001D54A",
                ['T'] = "\U0001D54B",
                ['U'] = "\U0001D54C",
                ['V'] = "\U0001D54D",
                ['W'] = "\U0001D54E",
                ['X'] = "\U0001D54F",
                ['Y'] = "\U0001D550"
            };

        /// <summary>
        /// Spacing command widths in em units.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, double> Spaces =
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                [","] = 0.167,
                [":"] = 0.222,
                [";"] = 0.278,
                ["!"] = -0.167,
                [" "] = 0.333,
                ["quad"] = 1.0,
                ["qquad"] = 2.0
            };

        /// <summary>
        /// Supported accent commands.
        /// </summary>
        public static readonly IReadOnlySet<string> Accents =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "hat", "widehat", "bar", "vec", "dot", "ddot", "tilde", "widetilde",
                "overline", "underline", "overbrace", "underbrace"
            };

        /// <summary>
        /// Supported text/font style commands.
        /// </summary>
        public static readonly IReadOnlySet<string> StyleCommands =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "text", "textrm", "textbf", "textit", "mbox",
                "mathbf", "mathrm", "mathit", "mathsf", "mathtt", "mathbb", "mathcal"
            };

        /// <summary>
        /// Converts a string inside \mathbb{...} to blackboard bold characters.
        /// </summary>
        public static string ToBlackboardBold(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            Span<char> buffer = stackalloc char[input.Length * 2];
            int idx = 0;
            foreach (char ch in input)
            {
                if (BlackboardBold.TryGetValue(ch, out string? mapped))
                {
                    mapped.AsSpan().CopyTo(buffer.Slice(idx));
                    idx += mapped.Length;
                }
                else
                {
                    buffer[idx++] = ch;
                }
            }
            return new string(buffer.Slice(0, idx));
        }
    }
}
