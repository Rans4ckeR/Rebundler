internal sealed class Icon
{
    public byte Width { get; init; }

    public byte Height { get; init; }

    public byte Colors { get; init; }

    public uint Size { get; init; }

    public uint Offset { get; init; }

    public ushort ColorPlanes { get; init; }

    public ushort BitsPerPixel { get; init; }

    public byte[]? Data { get; set; }
}