namespace Login38.Core.Net;

/// <summary>
/// Derives the stream key the server expects on encrypted connections.
/// </summary>
/// <remarks>
/// <para>
/// A server with packet encryption on opens by sending four bytes: a number encrypted with
/// the public half of the key pair it published in the server list. Decrypting it with the
/// private half, which the operator ships inside <c>list.txt</c>, gives a number the client
/// folds down to one byte and XORs every outgoing byte against.
/// </para>
/// <para>
/// It is a weak scheme and openly so — the private exponent travels with the client, and a
/// single-byte XOR falls to a paragraph of chat text. What it stops is a packet editor
/// pointed at the wire with no knowledge of this launcher, which is what operators are
/// actually losing players to.
/// </para>
/// </remarks>
public static class PacketEncryptKey
{
    /// <summary>How many bytes of challenge the server sends before anything else.</summary>
    public const int ChallengeLength = sizeof(uint);

    /// <summary>
    /// Turns the server's challenge into the byte every outgoing byte is XORed against.
    /// </summary>
    /// <param name="challenge">The four bytes the server sent, read little-endian.</param>
    /// <param name="privateExponent">The server list's <c>rsa_d</c>.</param>
    /// <param name="modulus">The server list's <c>rsa_n</c>.</param>
    /// <remarks>
    /// The fold is <c>value % 255 + 1</c>, which lands in 1..255 and never zero — a zero key
    /// would leave the traffic in the clear while both ends believed it was encrypted.
    /// </remarks>
    public static byte FromChallenge(uint challenge, uint privateExponent, uint modulus) =>
        (byte)((ModPow(challenge, privateExponent, modulus) % 255) + 1);

    /// <summary>
    /// <c>base^exponent mod modulus</c>, by squaring.
    /// </summary>
    /// <remarks>
    /// Every value is a 32-bit number, so the products stay inside 64 bits and this needs no
    /// big-integer arithmetic. A modulus of zero answers zero rather than dividing by it:
    /// that is a server list with no key in it, which is a configuration problem to report
    /// rather than an arithmetic one to throw on.
    /// </remarks>
    public static uint ModPow(uint value, uint exponent, uint modulus)
    {
        if (modulus == 0)
        {
            return 0;
        }

        ulong result = 1 % modulus;
        ulong current = value % modulus;

        while (exponent > 0)
        {
            if ((exponent & 1) == 1)
            {
                result = result * current % modulus;
            }

            current = current * current % modulus;
            exponent >>= 1;
        }

        return (uint)result;
    }
}
