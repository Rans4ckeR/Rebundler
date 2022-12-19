// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

internal sealed class Icons : List<Icon>
{
    public async ValueTask<byte[]> ToGroupData(int startIndex = 1)
    {
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        await using (writer.ConfigureAwait(false))
        {
            int i = 0;

            writer.Write((ushort)0);  //reserved, must be 0
            writer.Write((ushort)1);  // type is 1 for icons
            writer.Write((ushort)Count);  // number of icons in structure(1)

            foreach (Icon icon in this)
            {
                writer.Write(icon.Width);
                writer.Write(icon.Height);
                writer.Write(icon.Colors);
                writer.Write((byte)0); // reserved, must be 0
                writer.Write(icon.ColorPlanes);
                writer.Write(icon.BitsPerPixel);
                writer.Write(icon.Size);
                writer.Write((ushort)(startIndex + i));

                i++;
            }

            ms.Position = 0;

            return ms.ToArray();
        }
    }
}
