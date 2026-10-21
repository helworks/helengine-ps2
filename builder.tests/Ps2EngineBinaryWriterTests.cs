using helengine;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies configurable engine vectors retain the PS2 payload's fixed IEEE32 layout.
/// </summary>
public sealed class Ps2EngineBinaryWriterTests {
    /// <summary>
    /// Checks vector component order, single-precision width and byte order against independent wire fixtures.
    /// </summary>
    /// <param name="endianness">Byte order required by the payload.</param>
    /// <param name="expectedHex">Literal IEEE32 bytes for the five vector components.</param>
    [Theory]
    [InlineData(EngineBinaryEndianness.LittleEndian, "0000C03F000010C00000003F00008040000000C1")]
    [InlineData(EngineBinaryEndianness.BigEndian, "3FC00000C01000003F00000040800000C1000000")]
    public void WriteVectors_PreservesIeee32Components(EngineBinaryEndianness endianness, string expectedHex) {
        using MemoryStream stream = new();
        using Ps2EngineBinaryWriter writer = Ps2EngineBinaryWriter.Create(stream, endianness);

        writer.WriteFloat2(new float2(1.5, -2.25));
        writer.WriteFloat3(new float3(0.5, 4, -8));

        Assert.Equal(Convert.FromHexString(expectedHex), stream.ToArray());
    }
}
