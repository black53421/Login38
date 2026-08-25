using Login38.Core.Cryptography;
using Shouldly;

namespace Login38.Core.Tests.Cryptography;

public sealed class Rsa32Tests
{
    /// <summary>A real key triple taken from a live server, used as a fixed oracle.</summary>
    private const ulong KnownE = 628_624_543;
    private const ulong KnownD = 1_424_206_015;
    private const ulong KnownN = 2_001_201_551;

    // The server draws its nonce from [255, 900000254]; the bounds are the cases most
    // likely to expose an overflow.
    [Theory]
    [InlineData(255UL)]
    [InlineData(12_345UL)]
    [InlineData(0x1234567UL)]
    [InlineData(900_000_254UL)]
    public void RoundTripsAgainstAKnownServerKey(ulong nonce)
    {
        var cipher = Rsa32.ModPow(nonce, KnownE, KnownN);

        Rsa32.ModPow(cipher, KnownD, KnownN).ShouldBe(nonce);
    }

    [Fact]
    public void ModPowHandlesAModulusOfOne() => Rsa32.ModPow(12345, 67, 1).ShouldBe(0UL);

    [Fact]
    public void ModPowHandlesAZeroExponent() => Rsa32.ModPow(12345, 0, 999).ShouldBe(1UL);

    [Fact]
    public void GeneratedModulusFitsTheProtocolWindow()
    {
        for (var i = 0; i < 5; i++)
        {
            var key = Rsa32.Generate();

            ((ulong)key.N).ShouldBeGreaterThanOrEqualTo(1UL << 30);
            key.E.ShouldBeGreaterThan(1u);
            key.D.ShouldBeGreaterThan(1u);
        }
    }

    [Fact]
    public void GeneratedKeysRoundTrip()
    {
        for (var i = 0; i < 5; i++)
        {
            var key = Rsa32.Generate();
            ulong n = key.N;

            foreach (var nonce in new[] { 255UL, 0xABCD1234UL % (n - 1), n / 2, n - 2 })
            {
                var cipher = Rsa32.ModPow(nonce, key.E, n);

                Rsa32.ModPow(cipher, key.D, n).ShouldBe(nonce);
            }
        }
    }

    [Fact]
    public void GeneratedKeysDiffer()
    {
        var first = Rsa32.Generate();
        var second = Rsa32.Generate();

        first.ShouldNotBe(second);
    }
}
