using K4os.Compression.LZ4;
using MVLibraryNET.MVGL.x64;
using MVLibraryNET.Utilities;

namespace MVLibraryNET.MVGL;

public static class MvglHelper
{
    public static unsafe ArrayRental ExtractFile(in MvglFile file, Stream stream)
    {
        if (file.FileSize == 0) return ArrayRental.Empty;

        var rawData = ExtractFileNoDecompression(file, stream);
        var result = new ArrayRental(file.ExtractSize);
        LZ4Codec.Decode(rawData.Span, result.Span);
        
        rawData.Dispose();
        return result;
    }

    public static ArrayRental ExtractFileNoDecompression(in MvglFile file, Stream stream)
    {
        if (file.FileSize == 0) return ArrayRental.Empty;

        var rawData = new ArrayRental(file.FileSize);
        stream.Position = file.FileOffset;
        stream.ReadAtLeast(rawData.Span, rawData.Count);
        
        return rawData;
    }
    
    public static unsafe MvglFile[] GetFilesFromStream(Stream stream)
    {
        var header = new MDB1Header64();
        stream.ReadExactly(new(&header, sizeof(MDB1Header64)));
        
        var treeEntries = GC.AllocateUninitializedArray<FileTreeEntry64>((int)header.FileEntryCount);
        var nameEntries = GC.AllocateUninitializedArray<FileNameEntry64>((int)header.FileNameCount);
        var dataEntries = GC.AllocateUninitializedArray<FileDataEntry64>((int)header.DataEntryCount);

        fixed (FileTreeEntry64* fPtr = treeEntries)
        fixed (FileNameEntry64* nPtr = nameEntries)
        fixed (FileDataEntry64* dPtr = dataEntries)
        {
            stream.ReadExactly(new(fPtr, (int)(sizeof(FileTreeEntry64) * header.FileEntryCount)));
            stream.ReadExactly(new(nPtr, (int)(sizeof(FileNameEntry64) * header.FileNameCount)));
            stream.ReadExactly(new(dPtr, (int)(sizeof(FileDataEntry64) * header.DataEntryCount)));

            MvglFile[] files = GC.AllocateUninitializedArray<MvglFile>((int)header.DataEntryCount);
            for (int i = 0, filesIdx = 0; i < treeEntries.Length; i++)
            {
                ref var treeItem = ref treeEntries[i];
                if (treeItem.DataId == uint.MaxValue)
                    continue;
                
                ref var dataItem = ref dataEntries[treeItem.DataId];
                files[filesIdx] = new()
                {
                    FileName = nameEntries[i].ToString().Replace('\\', '/'),
                    FileOffset = (long)(dataItem.Offset + header.DataStart),
                    FileSize = (int)dataItem.CompressedSize,
                    ExtractSize = (int)dataItem.FullSize,
                };

                filesIdx++;
            }

            return files;
        }
    }
}
