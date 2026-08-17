namespace Game.Engine.ECS.Breakout;

/// <summary>
///     Breakout level data ported verbatim from the reference game
///     (<c>src/Temp/javascript-breakout/levels.js</c>). Each level owns a color palette
///     (lowercase char -> RGB) and an ASCII layout. Consecutive identical characters
///     in a row merge into one brick; toggling case keeps the same color but starts a
///     new brick (reference behavior). Spaces are empty cells.
/// </summary>
public static class BreakoutLevels
{
    public sealed record BreakoutLevel(string Name, IReadOnlyDictionary<char, SpriteColor> Colors, string[] Rows);

    private static SpriteColor Rgb(int hex) => new(
        (byte)((hex >> 16) & 0xFF),
        (byte)((hex >> 8) & 0xFF),
        (byte)(hex & 0xFF));

    private static IReadOnlyDictionary<char, SpriteColor> Palette(params (char Key, int Hex)[] entries)
    {
        var dict = new Dictionary<char, SpriteColor>(entries.Length);
        foreach (var (key, hex) in entries) dict[key] = Rgb(hex);
        return dict;
    }

    private static BreakoutLevel Level(string name, IReadOnlyDictionary<char, SpriteColor> palette, params string[] rows) =>
        new(name, palette, rows);

    private static readonly IReadOnlyDictionary<char, SpriteColor> Pastel = Palette(
        ('y', 0xFFF7A5), ('p', 0xFFA5E0), ('b', 0xA5B3FF), ('g', 0xBFFFA5), ('o', 0xFFCBA5));

    private static readonly IReadOnlyDictionary<char, SpriteColor> Arkanoid = Palette(
        ('w', 0xFCFCFC), ('o', 0xFC7460), ('l', 0x3CBCFC), ('g', 0x80D010), ('r', 0xD82800),
        ('b', 0x0070EC), ('p', 0xFC74B4), ('y', 0xFC9838), ('s', 0xBCBCBC), ('d', 0xF0BC3C));

    private static readonly IReadOnlyDictionary<char, SpriteColor> Vintage = Palette(
        ('a', 0xEFD279), ('b', 0x95CBE9), ('c', 0x024769), ('d', 0xAFD775), ('e', 0x2C5700),
        ('f', 0xDE9D7F), ('g', 0x7F9DDE), ('h', 0x00572C), ('i', 0x75D7AF), ('j', 0x694702),
        ('k', 0xE9CB95), ('l', 0x79D2EF));

    public static readonly IReadOnlyList<BreakoutLevel> All = new List<BreakoutLevel>
    {
        Level("Classic", Pastel,
            "", "", "", "", "", "",
            "yyyyyYYYYYyyyyyYYYYYyyyyyYYYYY",
            "pppppPPPPPpppppPPPPPpppppPPPPP",
            "bbbbbBBBBBbbbbbBBBBBbbbbbBBBBB",
            "gggggGGGGGgggggGGGGGgggggGGGGG",
            "oooooOOOOOoooooOOOOOoooooOOOOO"),

        Level("Arkanoid", Arkanoid,
            "", "",
            "          yy      yy          ",
            "            yy  yy            ",
            "            yy  yy            ",
            "          ssSSssSSss          ",
            "          ssSSssSSss          ",
            "        SSsswwsswwssSS        ",
            "        SSsswwsswwssSS        ",
            "      ssSSssSSssSSssSSss      ",
            "      ssSSssSSssSSssSSss      ",
            "      ss  ssSSssSSss  ss      ",
            "      ss  ss      ss  ss      ",
            "      ss  ss      ss  ss      ",
            "            ss  ss            ",
            "            ss  ss            "),

        Level("Stairs", Arkanoid,
            "",
            "oo",
            "ooll",
            "oollgg",
            "oollggbb",
            "oollggbbrr",
            "oollggbbrroo",
            "oollggbbrrooll",
            "oollggbbrroollgg",
            "oollggbbrroollggbb",
            "oollggbbrroollggbbrr",
            "oollggbbrroollggbbrroo",
            "oollggbbrroollggbbrrooll",
            "oollggbbrroollggbbrroollgg",
            "oollggbbrroollggbbrroollggbb",
            "ssSSssSSssSSssSSssSSssSSssSSrr"),

        Level("Pyramid", Arkanoid,
            "", "",
            "              ss              ",
            "          bbBBssggGG          ",
            "        BBbbWWwwWWGGgg        ",
            "      bbBBwwWWwwWWwwggGG      ",
            "      bbBBwwWWwwWWwwggGG      ",
            "      bbBBwwWWwwWWwwggGG      ",
            "      ss  ss  ss  ss  ss      ",
            "              ss              ",
            "              ss              ",
            "          oo  oo              ",
            "          ooOOoo              ",
            "            OO                "),

        Level("Spiral", Pastel,
            "", "",
            "  yyYYyyYYyyYY  YYyyYYyyYYyy  ",
            "  bbBBbbBBbbBB  BBbbBBbbBBbb  ",
            "  ggGGggGGggGG  GGggGGggGGgg  ",
            "  ooOOooOOooOO  OOooOOooOOoo  ",
            "", "",
            "  yyYYyyYYyyYY  YYyyYYyyYYyy  ",
            "  bbBBbbBBbbBB  BBbbBBbbBBbb  ",
            "  ggGGggGGggGG  GGggGGggGGgg  ",
            "  ooOOooOOooOO  OOooOOooOOoo  ",
            "", "",
            "  yyYYyyYYyyYY  YYyyYYyyYYyy  ",
            "  bbBBbbBBbbBB  BBbbBBbbBBbb  ",
            "  ggGGggGGggGG  GGggGGggGGgg  ",
            "  ooOOooOOooOO  OOooOOooOOoo  "),

        Level("Pyramid 2", Vintage,
            "", "", "",
            "   AAaaAAaaAAaaAAaaAAaaAAaa   ",
            "    BBbbBBbbBBbbBBbbBBbbBB    ",
            "     CCccCCccCCccCCccCCcc     ",
            "      DDddDDddDDddDDddDD      ",
            "       EEeeEEeeEEeeEEee       ",
            "        FFffFFffFFffFF        ",
            "         GGggGGggGGgg         ",
            "          HHhhHHhhHH          ",
            "           IIiiIIii           ",
            "            JJjjJJ            ",
            "             KKkk             ",
            "              LL              "),

        Level("Lines", Vintage,
            "", "",
            "  aabbccddeeffggFFEEDDCCBBAA  ",
            "   aabbccddeeffFFEEDDCCBBAA   ",
            "    aabbccddeeffEEDDCCBBAA    ",
            "     aabbccddeeEEDDCCBBAA     ",
            "      aabbccddeeDDCCBBAA      ",
            "       aabbccddDDCCBBAA       ",
            "        aabbccddCCBBAA        ",
            "         aabbccCCBBAA         ",
            "          aabbccBBAA          ",
            "      hh   aabbCCAA   hh      ",
            "     hhHH   aabbAA   hhHH     ",
            "    hhiiHH   aaAA   hhiiHH    ",
            "   hhiiIIHH   aa   hhiiIIHH   ",
            "  hhiijjIIHH      hhiijjIIHH  ",
            " hhiijjJJIIHH    hhiijjJJIIHH "),

        Level("Barrier", Pastel,
            "                              ",
            "                              ",
            "  bbBBbbBBbbBBbbBBbbBBbbBBbb  ",
            "  ooggGGggGGggGGggGGggGGggoo  ",
            "  ooggGGggGGggGGggGGggGGggoo  ",
            "  ooppPPppPPppPPppPPppPPppoo  ",
            "  ooppPPppPPppBBppPPppPPppoo  ",
            "  ooppPPppPPbbBBbbPPppPPppoo  ",
            "  ooppPPppBBbbOObbBBppPPppoo  ",
            "  ooppPPbbBBooOOooBBbbPPppoo  ",
            "  ooppBBbbOOooYYooOObbBBppoo  ",
            "  oobbBBOOooyyYYyyooOOBBbboo  ",
            "  oobbooOOYYyyYYyyYYOOoobboo  ",
            "  ooOOooyyYYyyYYyyYYyyooOOoo  ",
            "  ooOOYYyyYYyyYYyyYYyyYYOOoo  ",
            "  ooyyYYyyYYyyYYyyYYyyYYyyoo  ",
            "  ooyyYYyyYYyyYYyyYYyyYYyyoo  ",
            "  bbBBbbBBbbBBbbBBbbBBbbBBbb  "),

        Level("Cherry", Palette(
            ('b', 0x111111), ('w', 0xEEEEEE), ('c', 0xEC7150), ('s', 0xB33A2F)),
            "",
            "       bBb                    ",
            "      BcCcB                   ",
            "     bCwCcsb  b               ",
            "     bCcCcsb b                ",
            "      BcCsB B                 ",
            "    BbBsSsBbB       bBb       ",
            "   bcCcbBbcCcb     BcCcB      ",
            "  bcwcCsbcwcCsb   bCwCcsb  b  ",
            "  bcCcCsbcCcCsb   bCcCcsb b   ",
            "  bcCcsSbcCcsSb    BcCsB B    ",
            "   bsSsb bsSsb   BbBsSsBbB    ",
            "    bBb   bBb   bcCcbBbcCcb   ",
            "               bcwcCsbcwcCsb  ",
            "               bcCcCsbcCcCsb  ",
            "               bcCcsSbcCcsSb  ",
            "                bsSsb bsSsb   ",
            "                 bBb   bBb    ",
            "                              ",
            "                              ",
            "                              ",
            "                              "),

        Level("Fire", Palette(
            ('r', 0xD80000), ('b', 0x706800), ('o', 0xF8AB00), ('f', 0xF83800),
            ('w', 0xFFFFFF), ('e', 0xFFE0A8)),
            "",
            "    rRrRr                     ",
            "   RrRrRrRrR                  ",
            "   BbBoObo                    ",
            "  boboOoboOo       F    f   f ",
            "  bobBoOoboOo     f e         ",
            "  bBoOoObBbB       F  f     e ",
            "    oOoOoOo        Ff      E  ",
            "   bBrbBb        E  f fF F  f ",
            "  bBbrbBrbBb       FfFfFf  F  ",
            " bBbBrRrRbBbB     fFeFeFfFf   ",
            " oObrorRorboO    FfEeEeEfF    ",
            " oOorRrRrRoOo    FeEeWwEeFf   ",
            " oOrRrRrRrRoO   fFeFwWfEeFf   ",
            "   rRr  RrR     fFeFwWfEeFf   ",
            "  bBb    bBb    fFeEwWeEeFf   ",
            " bBbB    bBbB   fFfEeEeEfF    ",
            "                 FfFfFfFfF    ",
            "                   FfFfF      "),
    };
}