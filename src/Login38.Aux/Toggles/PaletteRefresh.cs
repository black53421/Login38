using Login38.Aux.Game;
using Login38.Interop;

namespace Login38.Aux.Toggles;

/// <summary>
/// A copy of what the client does when it needs to work out the palette again.
/// </summary>
/// <remarks>
/// <para>
/// The launcher cannot recompute a palette: the answer depends on the map, the tileset
/// table and a game-time object, and the routine that turns those into 256 colours is the
/// client's. So this is the client's own decision, written out as a dozen instructions and
/// run inside the client — decide whether the player is somewhere unlit, then call the
/// client's palette routine with either the indoor level or the calculated one.
/// </para>
/// <para>
/// Transcribed from the client's own path at <c>0x004EA6C8</c>. It reads two globals and a
/// table, and calls two of the client's functions; it takes no arguments and holds no
/// address of its own, so it runs correctly wherever it is written.
/// </para>
/// </remarks>
internal static class PaletteRefresh
{
    /// <summary>The client's palette object, called <c>thiscall</c>.</summary>
    internal static readonly GameAddress PaletteObject = new(0x00BDC7A4);

    /// <summary>Its "set the palette for this light level" method.</summary>
    internal static readonly GameAddress PaletteRoutine = new(0x00579E10);

    /// <summary>The game-time object the light level is calculated from.</summary>
    internal static readonly GameAddress GameTime = new(0x00C31E7C);

    /// <summary>The tileset the current map is drawn with.</summary>
    internal static readonly GameAddress TilesetId = new(0x00965B64);

    /// <summary>The tilesets the client considers unlit.</summary>
    internal static readonly GameAddress TilesetTable = new(0x009655F0);

    /// <summary>How many entries that table has.</summary>
    internal const uint TilesetTableLength = 0x15A;

    /// <summary>Map ids at or above this are underground.</summary>
    internal const uint FirstCaveMapId = 0x4000;

    /// <summary>What the client passes its palette routine when the player is unlit.</summary>
    internal const byte IndoorLevel = 1;

    /// <summary>The finished code.</summary>
    internal static byte[] Code { get; } = Build();

    /// <summary>
    /// Builds it.
    /// </summary>
    /// <remarks>
    /// The reference emitted the same instructions with the three branch displacements
    /// counted by hand in comments, checked by a <c>debug_assert</c> that release builds
    /// drop. Here the builder resolves them, and the test pins all 111 bytes.
    /// </remarks>
    internal static byte[] Build()
    {
        // Every address in this is an absolute immediate, so it does not matter where the
        // code ends up and the builder needs no real base to work from.
        var code = new ShellcodeBuilder(new GameAddress(0));

        code.PushAd();

        // Assume lit, then look for a reason to say otherwise.
        code.MovBytePtr(AllDayToggle.CaveDarkFlag, 0);

        code.MovEaxFrom(GameStructures.MapId);
        code.CmpEax(FirstCaveMapId);
        var unlit = code.ShortJumpIfGreaterOrEqual();

        // Above ground, but the tileset can still be an indoor one.
        code.MovEaxFrom(TilesetId);
        code.MovEdi(TilesetTable.Value);
        code.MovEcx(TilesetTableLength);
        code.Cld();
        code.RepneScasd();
        var decided = code.ShortJumpIfNotEqual();

        code.MarkLabel(unlit);
        code.MovBytePtr(AllDayToggle.CaveDarkFlag, 1);

        code.MarkLabel(decided);
        code.MovsxEdxByteFrom(AllDayToggle.CaveDarkFlag);
        code.TestEdxEdx();
        var lit = code.ShortJumpIfZero();

        // Unlit: the client passes a fixed level rather than calculating one.
        code.PushImm8(IndoorLevel);
        code.MovEcx(PaletteObject.Value);
        code.MovEax(PaletteRoutine.Value);
        code.CallEax();
        var done = code.ShortJumpAlways();

        // Lit: calculate the level from the time of day first. Four arguments, cdecl, so
        // this side cleans them up.
        code.MarkLabel(lit);
        code.PushImm8(0).PushImm8(0).PushImm8(0);
        code.MovEaxFrom(GameTime);
        code.PushEax();
        code.MovEax(AllDayToggle.BrightnessCalculator.Value);
        code.CallEax();
        code.AddEsp(0x10);

        code.PushEax();
        code.MovEcx(PaletteObject.Value);
        code.MovEax(PaletteRoutine.Value);
        code.CallEax();

        code.MarkLabel(done);
        code.PopAd();
        code.Ret();

        return code.Build();
    }
}
