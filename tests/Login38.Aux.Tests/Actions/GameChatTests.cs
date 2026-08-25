using Login38.Aux.Actions;
using Login38.Interop;
using Shouldly;

namespace Login38.Aux.Tests.Actions;

/// <summary>
/// Covers the code that puts a line in the game's own chat window.
/// </summary>
/// <remarks>
/// Pinned byte for byte against an independent transcription of the reference's builder,
/// because this runs on a thread inside the game: a wrong displacement or a missed stack
/// adjustment does not fail, it takes the client down.
/// </remarks>
public sealed class GameChatTests
{
    private static readonly GameAddress Text = new(0x0270_0000);

    [Fact]
    public void BuildsTheCallTheReferenceBuilt()
    {
        byte[] expected =
        [
            0x68, 0x00, 0x00, 0x00, 0x00,   // push 0            ; the fifth argument
            0x6A, 0xFF,                     // push -1           ; channel: show it here
            0x68, 0xE0, 0x07, 0x00, 0x00,   // push 0x07E0       ; green
            0x68, 0xFF, 0xFF, 0x00, 0x00,   // push 0xFFFF       ; from nobody
            0x68, 0x00, 0x00, 0x70, 0x02,   // push text
            0xB8, 0x00, 0x75, 0x43, 0x00,   // mov eax, 0x00437500
            0xFF, 0xD0,                     // call eax
            0x83, 0xC4, 0x14,               // add esp, 0x14     ; five arguments back off
            0x33, 0xC0,                     // xor eax, eax
            0xC2, 0x04, 0x00,               // ret 4             ; what a thread owes
        ];

        GameChat.Build(Text).ShouldBe(expected);
    }

    [Fact]
    public void IsShortEnoughToShareAPageWithTheText() =>
        GameChat.Build(Text).Length.ShouldBeLessThan(GameChat.SlotSize);

    // The stack has to come back exactly. Five arguments of four bytes each is what the
    // client leaves behind, and one byte out here corrupts the caller's frame.
    [Fact]
    public void TakesBackEveryArgumentItPushed() =>
        GameChat.ArgumentBytes.ShouldBe((byte)(5 * 4));

    // The client's own value for "show this here and do the scrolling", pushed as a byte
    // and sign-extended. Anything else routes it somewhere the player will not see.
    [Fact]
    public void SendsItOnTheChannelThatScrolls() =>
        GameChat.LocalChannel.ShouldBe((byte)0xFF);
}
