using Login38.Interop;

namespace Login38.Aux.Toggles;

/// <summary>
/// The code that draws damage numbers over what the player is hitting.
/// </summary>
/// <remarks>
/// <para>
/// Two detours into the client's packet handling, both on paths that already have the
/// numbers in registers. The client parses a hit, this code notes who and how much, builds
/// a line of text and hands it to the client's own "say something over that thing's head"
/// routine, then replays the instructions it displaced and jumps back.
/// </para>
/// <para>
/// Everything the code needs to remember lives at the end of the same allocation: the
/// running total, the last target, the text being built. Absolute addresses, because a
/// detour has nowhere to keep anything else — there is no register free and the client's
/// stack is not the launcher's to use.
/// </para>
/// </remarks>
internal static class DamageCave
{
    /// <summary>Where the client keeps the id the server knows the player by.</summary>
    internal static readonly GameAddress SelfId = new(0x00ABF4B4);

    /// <summary>And its pointer to the player's own record.</summary>
    internal static readonly GameAddress PlayerPointer = new(0x00C2D2B8);

    /// <summary>Two places within that record where the id can be.</summary>
    internal const byte PlayerId = 0x0C;

    /// <inheritdoc cref="PlayerId"/>
    internal const byte PlayerAlternateId = 0x14;

    /// <summary>The client's own routine for text over something's head.</summary>
    /// <remarks>
    /// <c>(target, text, colour, 1, 0, 0)</c>, cleaned up by the caller. Reusing it is why
    /// the numbers look like part of the game rather than like an overlay.
    /// </remarks>
    internal static readonly GameAddress OverheadText = new(0x0042B7B0);

    /// <summary>
    /// Red, in the client's colour format.
    /// </summary>
    /// <remarks>
    /// Not a choice about looks. The bubble the client draws carries its colour, and this
    /// value is what the at-feet detour recognises damage by — a white bubble is somebody
    /// talking and must be left where it is.
    /// </remarks>
    internal const uint TextColour = 0x0000F800;

    /// <summary>How long a run of hits on one target keeps adding up.</summary>
    /// <remarks>
    /// The second number in the display is "this fight", and a fight is over when nothing
    /// has hit for a while. Eight seconds is long enough to cover casting something slow.
    /// </remarks>
    internal const uint RunTimeout = 8_000;

    /// <summary>Room for the line being built.</summary>
    internal const int TextLength = 96;

    /// <summary>How much is asked for, which is more than enough for the code and the data.</summary>
    internal const int CaveSize = 0x2000;

    /// <summary>The single-target path: an ordinary swing, or a skill aimed at one thing.</summary>
    /// <remarks>
    /// The displaced run is the client's own "is there anything to draw" test, which is
    /// replayed at the end — including its branch, so the detour has to know where both
    /// arms of it go.
    /// </remarks>
    internal static HookSite Single => new(
        new GameAddress(0x005295D9),
        [
            0x83, 0x7D, 0xE0, 0x00,                     // cmp dword ptr [ebp-0x20], 0
            0x0F, 0x8E, 0xEA, 0x05, 0x00, 0x00,         // jle 0x00529BCD
        ],
        new GameAddress(0x005295E3));

    /// <summary>Where that replayed branch goes when it is taken.</summary>
    internal static readonly GameAddress SingleSkip = new(0x00529BCD);

    /// <summary>
    /// The many-target path, for a spell that hits everything nearby.
    /// </summary>
    /// <remarks>
    /// This detour lands after the client has parsed one target's id and hit flag and left
    /// the packet cursor in <c>eax</c>. The server this launcher is built for sends a real
    /// damage value after those two, which the stock client does not read — so the detour
    /// takes it and steps the cursor past it, and the next target starts where the client
    /// expects. That is also why there is no version of this for the stock server: the
    /// number simply is not in the packet.
    /// </remarks>
    internal static HookSite Area => new(
        new GameAddress(0x0052A821),
        [
            0x83, 0xC4, 0x10,                           // add esp, 0x10
            0x89, 0x45, 0xD0,                           // mov [ebp-0x30], eax
        ],
        new GameAddress(0x0052A827));

    /// <summary>Where each thing the cave remembers sits, relative to its start.</summary>
    /// <remarks>
    /// Worked out from the finished length of the code rather than fixed, so nothing has to
    /// be kept in step by hand. The code is emitted twice: once to learn how long it is,
    /// and once with the addresses that follow from that.
    /// </remarks>
    internal readonly record struct Layout(GameAddress Cave, int Code)
    {
        /// <summary>Where the client's stack pointer was when the detour was entered.</summary>
        public GameAddress StackSave => At(0);

        /// <summary>The running total for the current target.</summary>
        public GameAddress Total => At(1);

        /// <summary>What this hit was worth.</summary>
        public GameAddress Damage => At(2);

        /// <summary>And what it hit.</summary>
        public GameAddress Target => At(3);

        /// <summary>The state of the colour picker.</summary>
        public GameAddress Random => At(4);

        /// <summary>Where the loop over a spell's targets has got to.</summary>
        public GameAddress Index => At(5);

        /// <summary>And how many there are.</summary>
        public GameAddress Count => At(6);

        /// <summary>Four colour digits to pick between.</summary>
        public GameAddress Colours => At(7);

        /// <summary>Who the running total belongs to.</summary>
        public GameAddress LastTarget => At(8);

        /// <inheritdoc cref="LastTarget"/>
        public GameAddress LastTotal => At(9);

        /// <summary>When it was last added to.</summary>
        public GameAddress LastTick => At(10);

        /// <summary>Where the client's own <c>GetTickCount</c> is.</summary>
        public GameAddress Tick => At(11);

        /// <summary>Whether the helper should draw range-skill damage numbers.</summary>
        public GameAddress AreaEnabled => At(12);

        /// <summary>The line being built.</summary>
        public GameAddress Text => At(13);

        /// <summary>How long the whole allocation has to be.</summary>
        public int Size => Code + (Slots * 4) + TextLength;

        private const int Slots = 13;

        private GameAddress At(int slot) => Cave + (Code + (slot * 4));

        /// <summary>The data as it starts out.</summary>
        public byte[] Data(GameAddress getTickCount)
        {
            var data = new byte[(Slots * 4) + TextLength];

            // Anything will do to start the colour picker off, as long as two games running
            // side by side do not pick the same sequence. Where the cave landed is the one
            // number to hand that differs between them.
            BitConverter.TryWriteBytes(data.AsSpan(4 * 4), Cave.Value ^ 0xA5A55A5A);

            // The four digits the picker chooses between, as the client's own colour marks.
            "2222"u8.CopyTo(data.AsSpan(7 * 4));

            BitConverter.TryWriteBytes(data.AsSpan(11 * 4), getTickCount.Value);

            return data;
        }
    }

    /// <summary>Where each part of the cave's code begins, relative to the cave.</summary>
    /// <param name="Single">The detour for one swing at one thing.</param>
    /// <param name="Area">The detour for one target out of a spell.</param>
    /// <param name="Formatter">The decimal formatter both of them call.</param>
    /// <param name="Length">How long the code is, which is where the data starts.</param>
    internal readonly record struct Entries(int Single, int Area, int Formatter, int Length);

    /// <summary>
    /// Builds the whole cave.
    /// </summary>
    /// <param name="cave">Where it will be written.</param>
    /// <param name="getTickCount">The client's own <c>kernel32!GetTickCount</c>.</param>
    internal static (byte[] Code, Entries Entries) Build(GameAddress cave, GameAddress getTickCount)
    {
        // Once to learn how long the code is, then again knowing where the data went. The
        // length cannot change between the two: every address is four bytes whatever it
        // holds.
        var probe = Emit(cave, new Layout(cave, 0));
        var layout = new Layout(cave, probe.Entries.Length);
        var (code, entries) = Emit(cave, layout);

        return ([.. code, .. layout.Data(getTickCount)], entries);
    }


    internal static GameAddress AreaEnabledAddress(GameAddress cave, Entries entries) =>
        new Layout(cave, entries.Length).AreaEnabled;

    private static (byte[] Code, Entries Entries) Emit(GameAddress cave, Layout data)
    {
        var code = new ShellcodeBuilder(cave);
        var calls = new List<int>();

        var single = code.Length;
        Single_(code, data, calls);

        var area = code.Length;
        Area_(code, data, calls);

        // One copy of the decimal formatter, shared. It is called from both, which means
        // the calls are emitted before its address is known and filled in once it is.
        var formatter = code.Length;
        AppendNumber(code);

        var built = code.Build();

        foreach (var at in calls)
        {
            BitConverter.TryWriteBytes(built.AsSpan(at), formatter - (at + 4));
        }

        return (built, new Entries(single, area, formatter, built.Length));
    }

    /// <summary>Emits a call to the formatter, whose address is filled in later.</summary>
    private static void CallAppend(ShellcodeBuilder code, List<int> calls)
    {
        code.Byte(0xE8);
        calls.Add(code.Length);
        code.Dword(0);
    }

    /// <summary>
    /// Saves everything the client will expect back.
    /// </summary>
    /// <remarks>
    /// The stack pointer as well as the registers, because the paths below leave the stack
    /// unbalanced when they give up part way through — the shared exit puts it back rather
    /// than every branch having to unwind what it pushed.
    /// </remarks>
    private static void Enter(ShellcodeBuilder code, Layout data) =>
        code.PushFd().PushAd()
            .Bytes([0x89, 0x25]).Dword(data.StackSave.Value);         // mov [stack_save], esp

    /// <summary>Puts it all back, whether the detour did anything or not.</summary>
    private static void Leave(ShellcodeBuilder code, Layout data, List<ShellcodeBuilder.NearLabel> giveUp)
    {
        code.Bytes([0x8B, 0x25]).Dword(data.StackSave.Value);         // mov esp, [stack_save]

        foreach (var label in giveUp)
        {
            code.MarkLabel(label);
        }

        code.PopAd().PopFd();
    }

    /// <summary>
    /// Gives up unless the player is the one doing the hitting.
    /// </summary>
    /// <remarks>
    /// Three ids to compare against, because which one the packet carries depends on the
    /// path: the server's id for the character, and two fields in the client's own record
    /// for them. The attacker to test is expected in <c>edx</c>.
    /// </remarks>
    private static void OnlyMine(ShellcodeBuilder code, List<ShellcodeBuilder.NearLabel> giveUp)
    {
        code.Bytes([0x8B, 0x0D]).Dword(SelfId.Value)                  // mov ecx, [self_id]
            .Bytes([0x39, 0xCA]);                                     // cmp edx, ecx
        var mine = code.NearJump(0x84);

        code.Byte(0xA1).Dword(PlayerPointer.Value)                    // mov eax, [player]
            .Bytes([0x85, 0xC0]);                                     // test eax, eax
        giveUp.Add(code.NearJump(0x84));

        code.Bytes([0x8B, 0x48, PlayerId])                            // mov ecx, [eax+0x0C]
            .Bytes([0x39, 0xCA]);                                     // cmp edx, ecx
        var alsoMine = code.NearJump(0x84);

        code.Bytes([0x8B, 0x48, PlayerAlternateId])                   // mov ecx, [eax+0x14]
            .Bytes([0x39, 0xCA]);                                     // cmp edx, ecx
        giveUp.Add(code.NearJump(0x85));

        code.MarkLabel(mine).MarkLabel(alsoMine);
    }

    /// <summary>
    /// One swing at one thing.
    /// </summary>
    /// <remarks>
    /// The client has the attacker, the target and the damage in its own stack frame by the
    /// time the displaced test runs, so this is the straightforward path: read three
    /// numbers, show them, put the test back.
    /// </remarks>
    private static void Single_(ShellcodeBuilder code, Layout data, List<int> calls)
    {
        var giveUp = new List<ShellcodeBuilder.NearLabel>();

        Enter(code, data);

        code.Bytes([0x8B, 0x55, 0xEC]);                               // mov edx, [ebp-0x14] attacker
        OnlyMine(code, giveUp);

        code.Bytes([0x8B, 0x4D, 0xF0])                                // mov ecx, [ebp-0x10] target
            .Bytes([0x0F, 0xB7, 0x55, 0xCC]);                         // movzx edx, word [ebp-0x34] damage

        code.Bytes([0x85, 0xC9]);                                     // test ecx, ecx
        giveUp.Add(code.NearJump(0x84));
        code.Bytes([0x85, 0xD2]);                                     // test edx, edx
        giveUp.Add(code.NearJump(0x84));

        Show(code, data, calls);
        Leave(code, data, giveUp);

        // The displaced test, replayed — including where its branch goes, which is a long
        // way from here and so has to be an absolute jump rather than the client's own
        // relative one.
        code.Bytes([0x83, 0x7D, 0xE0, 0x00])                          // cmp dword ptr [ebp-0x20], 0
            .Bytes([0x0F, 0x8E]).Dword(Relative(code, SingleSkip, 4)) // jle 0x00529BCD
            .Byte(0xE9).Dword(Relative(code, Single.Resume, 4));      // jmp back
    }

    /// <summary>
    /// One target out of a spell that hit several.
    /// </summary>
    /// <remarks>
    /// Everything here is a check that something is where it should be, because this runs
    /// inside the client's own parse loop over an array it has only partly filled in. The
    /// hit flag says whether this target was actually hit; a zero there is a miss, and a
    /// miss is not worth a number over anybody's head.
    /// </remarks>
    private static void Area_(ShellcodeBuilder code, Layout data, List<int> calls)
    {
        var giveUp = new List<ShellcodeBuilder.NearLabel>();

        // Before saving anything: the packet cursor is live in eax and the extra damage
        // word has to come off it whatever else happens.
        code.Bytes([0x8B, 0x10])                                      // mov edx, [eax] damage
            .Bytes([0x83, 0xC0, 0x04]);                               // add eax, 4

        Enter(code, data);

        code.Bytes([0x89, 0x15]).Dword(data.Damage.Value);            // mov [damage], edx

        code.Bytes([0x83, 0x3D]).Dword(data.AreaEnabled.Value).Byte(0x00); // cmp dword ptr [enabled], 0
        giveUp.Add(code.NearJump(0x84));

        code.Bytes([0x8B, 0x55, 0xE4]);                               // mov edx, [ebp-0x1C] caster
        OnlyMine(code, giveUp);

        code.Bytes([0x8B, 0x75, 0xBC])                                // mov esi, [ebp-0x44] hits
            .Bytes([0x85, 0xF6]);                                     // test esi, esi
        giveUp.Add(code.NearJump(0x84));

        code.Bytes([0x8B, 0x9D, 0x10, 0xFE, 0xFF, 0xFF]);             // mov ebx, [ebp-0x1F0] index

        code.Bytes([0x8B, 0x7E, 0x08])                                // mov edi, [esi+8] hit flags
            .Bytes([0x85, 0xFF]);                                     // test edi, edi
        giveUp.Add(code.NearJump(0x84));
        code.Bytes([0x0F, 0xB7, 0x04, 0x5F])                          // movzx eax, word [edi+ebx*2]
            .Bytes([0x85, 0xC0]);                                     // test eax, eax
        giveUp.Add(code.NearJump(0x84));

        code.Bytes([0x8B, 0x7E, 0x04])                                // mov edi, [esi+4] targets
            .Bytes([0x85, 0xFF]);                                     // test edi, edi
        giveUp.Add(code.NearJump(0x84));
        code.Bytes([0x8B, 0x0C, 0x9F])                                // mov ecx, [edi+ebx*4]
            .Bytes([0x85, 0xC9]);                                     // test ecx, ecx
        giveUp.Add(code.NearJump(0x84));

        code.Bytes([0x8B, 0x15]).Dword(data.Damage.Value)             // mov edx, [damage]
            .Bytes([0x85, 0xD2]);                                     // test edx, edx
        giveUp.Add(code.NearJump(0x8E));                              // nothing to show for a miss

        Show(code, data, calls);
        Leave(code, data, giveUp);

        code.Bytes([0x83, 0xC4, 0x10])                                // add esp, 0x10
            .Bytes([0x89, 0x45, 0xD0])                                // mov [ebp-0x30], eax
            .Byte(0xE9).Dword(Relative(code, Area.Resume, 4));        // jmp back
    }

    /// <summary>
    /// Adds a hit to the running total and puts the result over the target's head.
    /// </summary>
    /// <remarks>Expects the target in <c>ecx</c> and the damage in <c>edx</c>.</remarks>
    private static void Show(ShellcodeBuilder code, Layout data, List<int> calls)
    {
        code.Bytes([0x89, 0x15]).Dword(data.Damage.Value)             // mov [damage], edx
            .Bytes([0x89, 0x0D]).Dword(data.Target.Value);            // mov [target], ecx

        AddToRun(code, data);
        BuildLine(code, data, calls);
        SayIt(code, data);
    }

    /// <summary>
    /// Keeps one running total, for whatever was hit last.
    /// </summary>
    /// <remarks>
    /// One slot rather than a table. A player only ever watches one thing's numbers, and a
    /// table would need clearing out — this starts again whenever the target changes or
    /// nothing has hit for a while, which is the same thing a player means by "this fight".
    /// </remarks>
    private static void AddToRun(ShellcodeBuilder code, Layout data)
    {
        // The clock routine is entitled to use these, and what they hold is the whole point.
        code.Byte(0x51).Byte(0x52)                                    // push ecx; push edx
            .Bytes([0xFF, 0x15]).Dword(data.Tick.Value)               // call [gettickcount]
            .Byte(0x5A).Byte(0x59);                                   // pop edx; pop ecx

        code.Bytes([0x89, 0xC3])                                      // mov ebx, eax
            .Bytes([0x2B, 0x1D]).Dword(data.LastTick.Value);          // sub ebx, [last_tick]

        code.Bytes([0x3B, 0x0D]).Dword(data.LastTarget.Value);        // cmp ecx, [last_target]
        var changed = code.NearJump(0x85);

        code.Bytes([0x81, 0xFB]).Dword(RunTimeout);                   // cmp ebx, 8000
        var stale = code.NearJump(0x87);

        code.Bytes([0x01, 0x15]).Dword(data.LastTotal.Value);         // add [last_total], edx
        var added = code.NearJumpAlways();

        code.MarkLabel(changed).MarkLabel(stale)
            .Bytes([0x89, 0x0D]).Dword(data.LastTarget.Value)         // mov [last_target], ecx
            .Bytes([0x89, 0x15]).Dword(data.LastTotal.Value);         // mov [last_total], edx

        code.MarkLabel(added)
            .Byte(0xA3).Dword(data.LastTick.Value)                    // mov [last_tick], eax
            .Byte(0xA1).Dword(data.LastTotal.Value)                   // mov eax, [last_total]
            .Byte(0xA3).Dword(data.Total.Value);                      // mov [total], eax
    }

    /// <summary>
    /// Writes the line the player sees.
    /// </summary>
    /// <remarks>
    /// <c>\fRf&gt;( \fRfN123\fRf&gt; ) 456</c> — the client's own colour marks around a
    /// bracketed hit and the running total. The digit after the second mark is picked per
    /// hit so that numbers landing on top of each other can still be told apart.
    /// </remarks>
    private static void BuildLine(ShellcodeBuilder code, Layout data, List<int> calls)
    {
        code.Byte(0xBF).Dword(data.Text.Value);                       // mov edi, text

        // "\\fRf>( \\fRf0" — fourteen bytes, written as three dwords and a word so it costs
        // no data of its own.
        code.Bytes([0xC7, 0x07, 0x5C, 0x5C, 0x66, 0x52])              // "\\fR"
            .Bytes([0xC7, 0x47, 0x04, 0x66, 0x3E, 0x28, 0x20])        // "f>( "
            .Bytes([0xC7, 0x47, 0x08, 0x5C, 0x5C, 0x66, 0x52])        // "\\fR"
            .Bytes([0x66, 0xC7, 0x47, 0x0C, 0x66, 0x30])              // "f0"
            .Bytes([0x83, 0xC7, 0x0E]);                               // add edi, 14

        // A cheap sequence stirred by what was hit, then one of four digits.
        code.Byte(0xA1).Dword(data.Random.Value)                      // mov eax, [random]
            .Bytes([0x69, 0xC0, 0xFD, 0x43, 0x03, 0x00])              // imul eax, eax, 0x343FD
            .Bytes([0x03, 0x05]).Dword(data.Damage.Value)             // add eax, [damage]
            .Bytes([0x03, 0x05]).Dword(data.Target.Value)             // add eax, [target]
            .Bytes([0x05, 0xC3, 0x9E, 0x26, 0x00])                    // add eax, 0x269EC3
            .Byte(0xA3).Dword(data.Random.Value)                      // mov [random], eax
            .Bytes([0xC1, 0xE8, 0x08])                                // shr eax, 8
            .Bytes([0x83, 0xE0, 0x03])                                // and eax, 3
            .Bytes([0x8A, 0x98]).Dword(data.Colours.Value)            // mov bl, [colours+eax]
            .Bytes([0x88, 0x5F, 0xFF]);                               // mov [edi-1], bl

        code.Byte(0xA1).Dword(data.Damage.Value);                     // mov eax, [damage]
        CallAppend(code, calls);

        code.Bytes([0xC7, 0x07, 0x5C, 0x5C, 0x66, 0x52])              // "\\fR"
            .Bytes([0xC7, 0x47, 0x04, 0x66, 0x3E, 0x20, 0x29])        // "f> )"
            .Bytes([0xC6, 0x47, 0x08, 0x20])                          // " "
            .Bytes([0x83, 0xC7, 0x09]);                               // add edi, 9

        code.Byte(0xA1).Dword(data.Total.Value);                      // mov eax, [total]
        CallAppend(code, calls);

        code.Bytes([0xC6, 0x07, 0x00]);                               // terminate
    }

    /// <summary>Hands the line to the client.</summary>
    private static void SayIt(ShellcodeBuilder code, Layout data) =>
        code.PushImm8(0).PushImm8(0).PushImm8(1)
            .PushImm32(TextColour)
            .PushImm32(data.Text)
            .Bytes([0xFF, 0x35]).Dword(data.Target.Value)             // push [target]
            .MovEax(OverheadText.Value)
            .CallEax()
            .AddEsp(0x18);

    /// <summary>
    /// Writes <c>eax</c> as decimal at <c>edi</c>, and leaves <c>edi</c> after it.
    /// </summary>
    /// <remarks>
    /// Digits come out backwards from a division, so they go on the stack and come off
    /// again. Zero is the one case that produces none at all and is written directly.
    /// </remarks>
    private static void AppendNumber(ShellcodeBuilder code)
    {
        code.Bytes([0x53, 0x51, 0x52, 0x56])                          // push ebx; ecx; edx; esi
            .Bytes([0x31, 0xC9])                                      // xor ecx, ecx  (how many)
            .Bytes([0xBB, 0x0A, 0x00, 0x00, 0x00])                    // mov ebx, 10
            .Bytes([0x85, 0xC0]);                                     // test eax, eax
        var some = code.ShortJumpIfNotZero();

        code.Bytes([0xC6, 0x07, 0x30])                                // mov byte [edi], '0'
            .Byte(0x47);                                              // inc edi
        var done = code.ShortJumpAlways();

        code.MarkLabel(some);
        var digit = code.Here();
        code.Bytes([0x31, 0xD2])                                      // xor edx, edx
            .Bytes([0xF7, 0xF3])                                      // div ebx
            .Bytes([0x80, 0xC2, 0x30])                                // add dl, '0'
            .Byte(0x52)                                               // push edx
            .Byte(0x41)                                               // inc ecx
            .Bytes([0x85, 0xC0])                                      // test eax, eax
            .ShortJumpBack(0x75, digit);

        var write = code.Here();
        code.Byte(0x5A)                                               // pop edx
            .Bytes([0x88, 0x17])                                      // mov [edi], dl
            .Byte(0x47)                                               // inc edi
            .ShortJumpBack(0xE2, write);                              // loop

        code.MarkLabel(done)
            .Bytes([0x5E, 0x5A, 0x59, 0x5B])                          // pop esi; edx; ecx; ebx
            .Ret();
    }

    /// <summary>The displacement from the end of an instruction being emitted to an address.</summary>
    private static uint Relative(ShellcodeBuilder code, GameAddress target, int remaining) =>
        unchecked((uint)(target.Value - (code.CurrentAddress.Value + (uint)remaining)));
}
