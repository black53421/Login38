using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Widens armour class and magic resistance from a byte to a full integer.
/// </summary>
/// <remarks>
/// <para>
/// The client stores AC in a signed byte and MR in a signed word, and caps MR at 250 in
/// two places before it is displayed. Servers that hand out gear beyond those ranges send
/// values the client wraps around: an AC of 200 shows as -56.
/// </para>
/// <para>
/// Widening the field in place is not possible — the surrounding structures are laid out
/// by the client and read by its own code. Instead this allocates three integers in a
/// code cave and redirects every read and write of the narrow fields to them, leaving the
/// original bytes intact so anything not redirected still sees a consistent, if
/// truncated, value.
/// </para>
/// <para>
/// The two character-select setters are replaced outright rather than hooked: they take
/// their arguments as bytes, so there is nothing to intercept — the caller has already
/// truncated. The replacements read the wide arguments the patched packet handlers now
/// push, and keep writing the legacy narrow slots so unpatched readers still work.
/// </para>
/// </remarks>
public sealed class ArmourResistanceExpansionPatch : IGamePatch
{
    // ---- data the client already has, kept for the legacy slots ------------------------

    private static readonly GameAddress MaxHitPoints = new(0x00C3_1E90);
    private static readonly GameAddress MaxManaPoints = new(0x00C3_1E8C);

    /// <summary>The client's own signed-byte AC, still written for unpatched readers.</summary>
    private static readonly GameAddress LegacyArmourClass = new(0x00C3_1E7B);

    /// <summary>The client's own signed-word MR bonus.</summary>
    private static readonly GameAddress LegacyMagicResistance = new(0x00C3_1EAC);

    // ---- sites ------------------------------------------------------------------------

    /// <summary>The AC field of the status packet's format string.</summary>
    private static readonly GameAddress StatusFormatArmour = new(0x008D_4701);

    private static readonly GameAddress StatusArmourOutputPointer = new(0x0052_33D7);

    private static readonly GameAddress StatusArmourTextRead = new(0x0078_1554);
    private static readonly GameAddress OverlayArmourTextRead = new(0x0079_97A8);
    private static readonly GameAddress StatusArmourFormatRead = new(0x0056_65F3);

    private static readonly GameAddress CharacterAckFormatArmour = new(0x008D_72CE);
    private static readonly GameAddress CharacterAckOutputHook = new(0x0054_3995);
    private static readonly GameAddress CharacterAckOutputReturn = new(0x0054_39A5);
    private static readonly GameAddress CharacterAckArmourArgument = new(0x0054_39B9);

    private static readonly GameAddress CharacterAck2FormatArmour = new(0x008D_72D8);
    private static readonly GameAddress CharacterAck2ArmourTruncation = new(0x0054_3A86);

    private static readonly GameAddress CharacterSelectSetter1 = new(0x0054_4910);
    private static readonly GameAddress CharacterSelectSetter2 = new(0x0054_4940);
    private static readonly GameAddress CharacterSelectArmourDisplay = new(0x0076_B637);
    private static readonly GameAddress CharacterSelectArmourDisplayReturn = new(0x0076_B63C);
    private static readonly GameAddress CharacterSelectArmourGetter = new(0x0076_D810);

    private static readonly GameAddress ResistanceFormat = new(0x008D_5065);
    private static readonly GameAddress ResistanceOutputPointer = new(0x0053_40E3);
    private static readonly GameAddress ResistanceReadToEdx = new(0x0073_B273);
    private static readonly GameAddress ResistanceReadToEcx = new(0x0073_B29E);
    private static readonly GameAddress ResistanceReadToEax = new(0x0073_B2BB);
    private static readonly GameAddress ResistanceDisplayCap = new(0x0078_141C);
    private static readonly GameAddress ResistanceSkillUiCap = new(0x0074_0631);

    // ---- cave layout -------------------------------------------------------------------

    private const int CaveSize = 0x180;

    private const int InGameArmourOffset = 0x00;
    private const int CharacterSelectArmourOffset = 0x04;
    private const int ResistanceBonusOffset = 0x08;
    private const int Setter2CodeOffset = 0x20;
    private const int ArmourDisplayCodeOffset = 0xC0;
    private const int Setter1CodeOffset = 0x100;
    private const int CharacterAckOutputCodeOffset = 0x140;

    /// <summary>The three widened fields, at the front of the cave.</summary>
    private const int DataSize = 12;

    /// <summary>Where the widened values live, once the cave has been allocated.</summary>
    /// <param name="InGameArmour">AC while playing.</param>
    /// <param name="CharacterSelectArmour">AC on the character-select screen.</param>
    /// <param name="ResistanceBonus">MR bonus from equipment.</param>
    internal readonly record struct Storage(
        GameAddress InGameArmour,
        GameAddress CharacterSelectArmour,
        GameAddress ResistanceBonus);

    private readonly ILogger<ArmourResistanceExpansionPatch> _logger;

    public ArmourResistanceExpansionPatch(ILogger<ArmourResistanceExpansionPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "ac-mr-expansion";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.Startup;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Aux.AcMrLimitEnabled;
    }

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var cave = context.Process.AllocateExecutable(CaveSize);
        var storage = new Storage(
            cave + InGameArmourOffset,
            cave + CharacterSelectArmourOffset,
            cave + ResistanceBonusOffset);

        // Zeroed before anything reads it: the client shows whatever is here until the
        // first packet arrives, and fresh pages are zero only by convention.
        context.Process.WriteBytes(cave, new byte[DataSize]);

        var setter1 = cave + Setter1CodeOffset;
        var setter2 = cave + Setter2CodeOffset;
        var armourDisplay = cave + ArmourDisplayCodeOffset;
        var characterAckOutput = cave + CharacterAckOutputCodeOffset;

        context.Process.WriteCode(setter1, BuildSetter1(storage));
        context.Process.WriteCode(setter2, BuildSetter2(storage));
        context.Process.WriteCode(armourDisplay, BuildArmourDisplayHook(storage, armourDisplay));
        context.Process.WriteCode(characterAckOutput, BuildCharacterAckOutputHook(storage, characterAckOutput));

        _logger.LogDebug("AC/MR cave at {Cave}: ac={InGame} select={Select} mr={Resistance}",
            cave, storage.InGameArmour, storage.CharacterSelectArmour, storage.ResistanceBonus);

        var edits = new List<(string Description, SiteEdit Edit)>();

        void Edit(GameAddress address, string description, ReadOnlySpan<byte> expected, ReadOnlySpan<byte> replacement) =>
            edits.Add((description, CodeEdit.ReplaceAt(context, address, description, expected, replacement)));

        void Divert(GameAddress address, string description, ReadOnlySpan<byte> expected, GameAddress target) =>
            edits.Add((description, CodeEdit.Divert(context, address, description, expected, target)));

        // ---- in-game AC ----------------------------------------------------------------

        // The format string reads the argument as a character; 'd' makes it read an int.
        Edit(StatusFormatArmour, "status packet AC format", "c"u8, "d"u8);
        Edit(StatusArmourOutputPointer, "status packet AC output pointer",
            Push(LegacyArmourClass), Push(storage.InGameArmour));

        // movsx eax, byte [legacy] → mov eax, [wide]
        Edit(StatusArmourTextRead, "status AC read",
            [0x0F, 0xBE, 0x05, .. Address(LegacyArmourClass)],
            [0xA1, .. Address(storage.InGameArmour), 0x90, 0x90]);

        // movsx ecx, byte [legacy] → mov ecx, [wide]
        Edit(OverlayArmourTextRead, "overlay AC read",
            [0x0F, 0xBE, 0x0D, .. Address(LegacyArmourClass)],
            [0x8B, 0x0D, .. Address(storage.InGameArmour), 0x90]);

        // movzx edx, byte [legacy] → mov edx, [wide]
        Edit(StatusArmourFormatRead, "status AC format read",
            [0x0F, 0xB6, 0x15, .. Address(LegacyArmourClass)],
            [0x8B, 0x15, .. Address(storage.InGameArmour), 0x90]);

        // ---- character-select AC -------------------------------------------------------

        Edit(CharacterAckFormatArmour, "character ack AC format", "c"u8, "d"u8);
        Divert(CharacterAckOutputHook, "character ack AC output",
            [0x8D, 0x45, 0xFE, 0x50, 0x8D, 0x4D, 0xF3, 0x51,
             0x8D, 0x55, 0xF4, 0x52, 0x8D, 0x45, 0xF8, 0x50],
            characterAckOutput);

        // movsx dx, byte [ebp-0xD] / movzx eax, dx / push eax → push the wide value
        Edit(CharacterAckArmourArgument, "character ack AC argument",
            [0x66, 0x0F, 0xBE, 0x55, 0xF3, 0x0F, 0xB7, 0xC2, 0x50],
            [0xA1, .. Address(storage.CharacterSelectArmour), 0x50, 0x90, 0x90, 0x90]);

        Edit(CharacterAck2FormatArmour, "character ack 2 AC format", "h"u8, "d"u8);

        // movzx eax, word [ebp-0x1C] → mov eax, [ebp-0x1C]
        Edit(CharacterAck2ArmourTruncation, "character ack 2 AC truncation",
            [0x0F, 0xB7, 0x45, 0xE4], [0x8B, 0x45, 0xE4, 0x90]);

        edits.Add(("character-select setter 1", ReplaceFunction(context, CharacterSelectSetter1, setter1)));
        edits.Add(("character-select setter 2", ReplaceFunction(context, CharacterSelectSetter2, setter2)));

        // movsx ecx, word [eax+0xE] / push ecx → read the wide value instead
        Divert(CharacterSelectArmourDisplay, "character-select AC display",
            [0x0F, 0xBF, 0x48, 0x0E, 0x51], armourDisplay);

        // The getter's whole body is a truncating read, so it is replaced with a wide one.
        Edit(CharacterSelectArmourGetter, "character-select AC getter",
            [0x55, 0x8B, 0xEC, 0x51, 0x89, 0x4D, 0xFC, 0x8B, 0x45, 0xFC,
             0x66, 0x8B, 0x40, 0x0E, 0x8B, 0xE5, 0x5D, 0xC3],
            [0xA1, .. Address(storage.CharacterSelectArmour), 0xC3,
             0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90]);

        // ---- MR --------------------------------------------------------------------------

        Edit(ResistanceFormat, "MR update format", "h"u8, "d"u8);
        Edit(ResistanceOutputPointer, "MR update output pointer",
            Push(LegacyMagicResistance), Push(storage.ResistanceBonus));

        Edit(ResistanceReadToEdx, "MR read to edx",
            [0x0F, 0xBF, 0x15, .. Address(LegacyMagicResistance)],
            [0x8B, 0x15, .. Address(storage.ResistanceBonus), 0x90]);

        Edit(ResistanceReadToEcx, "MR read to ecx",
            [0x0F, 0xBF, 0x0D, .. Address(LegacyMagicResistance)],
            [0x8B, 0x0D, .. Address(storage.ResistanceBonus), 0x90]);

        Edit(ResistanceReadToEax, "MR read to eax",
            [0x0F, 0xBF, 0x05, .. Address(LegacyMagicResistance)],
            [0xA1, .. Address(storage.ResistanceBonus), 0x90, 0x90]);

        // cmp [local], 250 / jle skip / mov [local], 250 — the cap, removed outright.
        Edit(ResistanceDisplayCap, "MR display cap", ResistanceCap(0xFC), Nops(16));
        Edit(ResistanceSkillUiCap, "MR skill UI cap", ResistanceCap(0xF8), Nops(16));

        Report(edits);
    }

    private void Report(List<(string Description, SiteEdit Edit)> edits)
    {
        var missing = edits.Where(e => e.Edit.Status == SiteStatus.NotFound).Select(e => e.Description).ToList();
        var effective = edits.Count - missing.Count;

        foreach (var (description, edit) in edits)
        {
            _logger.LogDebug("{Description}: {Status} at {Address}", description, edit.Status, edit.Address);
        }

        // Some of these sites are independent — a missing overlay read costs one place the
        // number is shown. Others are not, and there is no way to tell from here, so the
        // threshold is "most of it worked".
        if (missing.Count > edits.Count / 4)
        {
            throw new GameProcessException(
                $"Only {effective} of {edits.Count} AC/MR sites matched. Missing: {string.Join(", ", missing)}.");
        }

        if (missing.Count > 0)
        {
            _logger.LogWarning("AC/MR: {Effective} of {Total} sites applied; missing {Missing}",
                effective, edits.Count, string.Join(", ", missing));
        }
        else
        {
            _logger.LogInformation("AC/MR widened to 32 bits across {Total} sites", edits.Count);
        }
    }

    /// <summary>
    /// Points a whole function at a replacement, accepting either its original prologue or
    /// a jump left by an earlier run.
    /// </summary>
    private static SiteEdit ReplaceFunction(GamePatchContext context, GameAddress address, GameAddress target)
    {
        var jump = InlineHook.BuildJump(address, target);
        var current = context.Process.ReadBytes(address, InlineHook.JumpSize);

        if (current.AsSpan().SequenceEqual(jump))
        {
            return new SiteEdit(SiteStatus.AlreadyPatched, address);
        }

        // Only the prologue byte is checked: the rest of the function differs between
        // builds and is about to be jumped over anyway.
        if (current[0] is not (0x55 or 0xE9))
        {
            return new SiteEdit(SiteStatus.NotFound, address);
        }

        context.Image.Apply(context.Process, address, jump);
        return new SiteEdit(SiteStatus.Patched, address);
    }

    // ---- code the cave holds -------------------------------------------------------------

    /// <summary>
    /// Replaces the single-argument character-select setter with one that stores the wide
    /// values and keeps the legacy narrow slots in step.
    /// </summary>
    internal static byte[] BuildSetter1(Storage storage) =>
    [
        0x55, 0x8B, 0xEC, 0x51,                               // push ebp; mov ebp,esp; push ecx
        0x89, 0x4D, 0xFC,                                     // mov [ebp-4], ecx
        0x8B, 0x45, 0xFC,                                     // mov eax, [ebp-4]   (this)

        0x8B, 0x4D, 0x08,                                     // mov ecx, [ebp+8]   (max HP)
        0x89, 0x0D, .. Address(MaxHitPoints),                 // mov [maxHp], ecx
        0x66, 0x89, 0x48, 0x0A,                               // mov [eax+0xA], cx

        0x8B, 0x4D, 0x0C,                                     // mov ecx, [ebp+0xC] (max MP)
        0x89, 0x0D, .. Address(MaxManaPoints),                // mov [maxMp], ecx
        0x66, 0x89, 0x48, 0x0C,                               // mov [eax+0xC], cx

        0x8B, 0x4D, 0x10,                                     // mov ecx, [ebp+0x10] (AC)
        0x89, 0x0D, .. Address(storage.CharacterSelectArmour),// mov [wideAc], ecx
        0x66, 0x8B, 0x4D, 0x10,                               // mov cx, [ebp+0x10]
        0x66, 0x89, 0x48, 0x0E,                               // mov [eax+0xE], cx

        0x8B, 0xE5, 0x5D, 0xC2, 0x0C, 0x00,                   // mov esp,ebp; pop ebp; ret 0xC
    ];

    /// <summary>The wider character-select setter, which also carries the six resistances.</summary>
    internal static byte[] BuildSetter2(Storage storage)
    {
        List<byte> code =
        [
            0x55, 0x8B, 0xEC, 0x51,                               // push ebp; mov ebp,esp; push ecx
            0x89, 0x4D, 0xFC,                                     // mov [ebp-4], ecx
            0x8B, 0x45, 0xFC,                                     // mov eax, [ebp-4]   (this)

            0x8A, 0x4D, 0x08, 0x88, 0x48, 0x10,                   // byte arg 1 → [eax+0x10]
            0x8A, 0x4D, 0x0C, 0x88, 0x48, 0x11,                   // byte arg 2 → [eax+0x11]

            0x8B, 0x4D, 0x10,                                     // mov ecx, [ebp+0x10] (max HP)
            0x89, 0x0D, .. Address(MaxHitPoints),
            0x66, 0x89, 0x48, 0x0A,

            0x8B, 0x4D, 0x14,                                     // mov ecx, [ebp+0x14] (max MP)
            0x89, 0x0D, .. Address(MaxManaPoints),
            0x66, 0x89, 0x48, 0x0C,

            0x8B, 0x4D, 0x18,                                     // mov ecx, [ebp+0x18] (AC)
            0x89, 0x0D, .. Address(storage.CharacterSelectArmour),
            0x66, 0x8B, 0x4D, 0x18,
            0x66, 0x89, 0x48, 0x0E,
        ];

        // The six elemental resistances, each still a signed byte on the wire, sign-extended
        // into the wide slots the display code now reads.
        foreach (var (argument, field) in ((byte Argument, byte Field)[])
                 [(0x1C, 0x14), (0x20, 0x18), (0x24, 0x1C), (0x28, 0x20), (0x2C, 0x24), (0x30, 0x28)])
        {
            code.AddRange([0x0F, 0xBE, 0x4D, argument]);          // movsx ecx, byte [ebp+arg]
            code.AddRange([0x89, 0x48, field]);                   // mov [eax+field], ecx
        }

        code.AddRange([0x8B, 0xE5, 0x5D, 0xC2, 0x2C, 0x00]);      // mov esp,ebp; pop ebp; ret 0x2C
        return [.. code];
    }

    /// <summary>Feeds the character-select display the wide AC instead of the narrow one.</summary>
    internal static byte[] BuildArmourDisplayHook(Storage storage, GameAddress cave) =>
        new ShellcodeBuilder(cave)
            .Bytes([0x8B, 0x0D]).Dword(storage.CharacterSelectArmour.Value)  // mov ecx, [wideAc]
            .Byte(0x51)                                                      // push ecx
            .JumpTo(CharacterSelectArmourDisplayReturn)
            .Build();

    /// <summary>
    /// Replaces the packet handler's output-pointer setup so the AC field is written into
    /// the wide slot.
    /// </summary>
    /// <remarks>
    /// The handler pushes four <c>lea</c>-computed stack addresses for the parser to fill.
    /// Only the AC one changes; the other three are replayed exactly as they were.
    /// </remarks>
    internal static byte[] BuildCharacterAckOutputHook(Storage storage, GameAddress cave) =>
        new ShellcodeBuilder(cave)
            .Bytes([0x8D, 0x45, 0xFE, 0x50])                                 // lea eax,[ebp-2]; push eax
            .PushImm32(storage.CharacterSelectArmour.Value)                  // push wideAc
            .Bytes([0x8D, 0x55, 0xF4, 0x52])                                 // lea edx,[ebp-0xC]; push edx
            .Bytes([0x8D, 0x45, 0xF8, 0x50])                                 // lea eax,[ebp-8]; push eax
            .JumpTo(CharacterAckOutputReturn)
            .Build();

    // ---- small helpers --------------------------------------------------------------------

    private static byte[] Address(GameAddress address) => BitConverter.GetBytes(address.Value);

    private static byte[] Push(GameAddress address) => [0x68, .. Address(address)];

    /// <summary><c>cmp [ebp-x], 250</c> / <c>jle +7</c> / <c>mov [ebp-x], 250</c>.</summary>
    private static byte[] ResistanceCap(byte local) =>
    [
        0x81, 0x7D, local, 0xFA, 0x00, 0x00, 0x00,
        0x7E, 0x07,
        0xC7, 0x45, local, 0xFA, 0x00, 0x00, 0x00,
    ];

    private static byte[] Nops(int count) => [.. Enumerable.Repeat((byte)0x90, count)];
}
