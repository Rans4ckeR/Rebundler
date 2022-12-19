// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

internal sealed class IconReader
{
    public readonly Icons Icons = new();

    public IconReader(Stream input)
    {
        using BinaryReader reader = new(input);
        reader.ReadUInt16(); // ignore. Should be 0

        ushort type = reader.ReadUInt16();

        if (type != 1)
            throw new("Invalid type. The stream is not an icon file");

        ushort numOfImages = reader.ReadUInt16();

        for (int i = 0; i < numOfImages; i++)
        {
            byte width = reader.ReadByte();
            byte height = reader.ReadByte();
            byte colors = reader.ReadByte();

            reader.ReadByte(); // ignore. Should be 0

            ushort colorPlanes = reader.ReadUInt16(); // should be 0 or 1
            ushort bitsPerPixel = reader.ReadUInt16();
            uint size = reader.ReadUInt32();
            uint offset = reader.ReadUInt32();

            Icons.Add(new()
            {
                Width = width,
                Height = height,
                Colors = colors,
                Size = size,
                Offset = offset,
                ColorPlanes = colorPlanes,
                BitsPerPixel = bitsPerPixel
            });
        }

        foreach (Icon icon in Icons)
        {
            if (reader.BaseStream.Position < icon.Offset)
            {
                int dummyBytesToRead = (int)(icon.Offset - reader.BaseStream.Position);
                reader.ReadBytes(dummyBytesToRead);
            }

            byte[] data = reader.ReadBytes((int)icon.Size);

            icon.Data = data;
        }
    }
}
