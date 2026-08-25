using System.Security.Cryptography;

namespace Login38.Core.Cryptography;

/// <summary>
/// A 32-bit RSA key triple.
/// </summary>
/// <param name="E">Public exponent. The server encrypts with this.</param>
/// <param name="D">Private exponent. The client decrypts with this.</param>
/// <param name="N">Modulus, always in [2^30, 2^32).</param>
public readonly record struct Rsa32Key(uint E, uint D, uint N);

/// <summary>
/// Key generation for the login handshake's tiny RSA.
/// </summary>
/// <remarks>
/// <para>
/// This matches the server's Java implementation. On accept, the server picks
/// <c>random</c> in [255, 900000254], sends <c>random^E mod N</c> to the client as
/// four little-endian bytes, and the client recovers it with <c>authdata^D mod N</c>.
/// From that it derives <c>xorByte = random % 255 + 1</c>, which masks the high and
/// low bytes of every later packet.
/// </para>
/// <para>
/// A 32-bit modulus is not a security boundary and is not treated as one — it is
/// factorable in milliseconds. It exists because the protocol says so. The size
/// constraint is what drives the implementation: <c>N</c> has to fit in a
/// <see cref="uint"/> because the Java side takes the low bits of a
/// <c>BigInteger</c>, so the two primes are roughly 16 bits each and intermediate
/// products are computed in <see cref="UInt128"/> to avoid overflow.
/// </para>
/// </remarks>
public static class Rsa32
{
    /// <summary>Lower bound for each prime, chosen so that p*q is at least 2^30.</summary>
    private const ulong MinPrime = 40_000;

    /// <summary>Exclusive upper bound: primes stay inside 16 bits.</summary>
    private const ulong MaxPrimeExclusive = 65_536;

    private const ulong MinModulus = 1UL << 30;

    /// <summary>How many exponent candidates to try before picking new primes.</summary>
    private const int ExponentAttempts = 1024;

    /// <summary>Deterministic Miller-Rabin witnesses; sufficient for all n &lt; 2^64.</summary>
    private static ReadOnlySpan<ulong> Witnesses => [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37];

    /// <summary>Computes <c>value^exponent mod modulus</c>.</summary>
    public static ulong ModPow(ulong value, ulong exponent, ulong modulus)
    {
        if (modulus == 1)
        {
            return 0;
        }

        ulong result = 1;
        value %= modulus;

        while (exponent > 0)
        {
            if ((exponent & 1) == 1)
            {
                result = MulMod(result, value, modulus);
            }

            exponent >>= 1;
            value = MulMod(value, value, modulus);
        }

        return result;
    }

    /// <summary>Generates a fresh key triple.</summary>
    public static Rsa32Key Generate()
    {
        while (true)
        {
            var p = RandomPrime();
            ulong q;
            do
            {
                q = RandomPrime();
            }
            while (q == p);

            var n = p * q;
            if (n is < MinModulus or > uint.MaxValue)
            {
                continue;
            }

            var phi = (p - 1) * (q - 1);

            // Pick the private exponent first and derive the public one, so that D is
            // uniformly distributed rather than being the inverse of a fixed small E.
            for (var attempt = 0; attempt < ExponentAttempts; attempt++)
            {
                var d = NextUInt64(3, phi);
                if ((d & 1) == 0 || Gcd(d, phi) != 1)
                {
                    continue;
                }

                if (ModInverse(d, phi) is not { } e || e <= 1 || e >= phi)
                {
                    continue;
                }

                // Prove the pair actually round-trips before handing it out; a bad key
                // here would show up as an unexplained login failure much later.
                var probe = Math.Max(n / 7, 2);
                if (ModPow(ModPow(probe, e, n), d, n) == probe)
                {
                    return new Rsa32Key((uint)e, (uint)d, (uint)n);
                }
            }
        }
    }

    /// <summary>Multiplies in <see cref="UInt128"/> so that n*n cannot overflow.</summary>
    private static ulong MulMod(ulong a, ulong b, ulong modulus) =>
        (ulong)((UInt128)a * b % modulus);

    private static ulong Gcd(ulong a, ulong b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }

    /// <summary>
    /// Extended Euclid, iterative. Returns null when <paramref name="a"/> and
    /// <paramref name="modulus"/> are not coprime.
    /// </summary>
    private static ulong? ModInverse(ulong a, ulong modulus)
    {
        long previousRemainder = (long)modulus;
        long remainder = (long)a;
        long previousCoefficient = 0;
        long coefficient = 1;

        while (remainder != 0)
        {
            var quotient = previousRemainder / remainder;
            (previousRemainder, remainder) = (remainder, previousRemainder - quotient * remainder);
            (previousCoefficient, coefficient) = (coefficient, previousCoefficient - quotient * coefficient);
        }

        if (previousRemainder != 1)
        {
            return null;
        }

        var m = (long)modulus;
        return (ulong)((previousCoefficient % m + m) % m);
    }

    private static bool IsPrime(ulong n)
    {
        if (n < 2)
        {
            return false;
        }

        foreach (var witness in Witnesses)
        {
            if (n == witness)
            {
                return true;
            }

            if (n % witness == 0)
            {
                return false;
            }
        }

        var d = n - 1;
        var r = 0;
        while ((d & 1) == 0)
        {
            d >>= 1;
            r++;
        }

        foreach (var witness in Witnesses)
        {
            var x = ModPow(witness, d, n);
            if (x == 1 || x == n - 1)
            {
                continue;
            }

            var composite = true;
            for (var i = 0; i < r - 1; i++)
            {
                x = MulMod(x, x, n);
                if (x == n - 1)
                {
                    composite = false;
                    break;
                }
            }

            if (composite)
            {
                return false;
            }
        }

        return true;
    }

    private static ulong RandomPrime()
    {
        while (true)
        {
            // Forcing the low bit skips every even candidate for free.
            var candidate = NextUInt64(MinPrime, MaxPrimeExclusive) | 1;
            if (IsPrime(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// A random value in [lo, hi). The modulo bias is irrelevant for a key this size,
    /// and the range exceeds <see cref="int"/> so <c>GetInt32</c> cannot be used.
    /// </summary>
    private static ulong NextUInt64(ulong lo, ulong hi)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(buffer);
        return lo + BitConverter.ToUInt64(buffer) % (hi - lo);
    }
}
