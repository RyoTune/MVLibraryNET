using MVLibraryNET.Definitions.MVGL.Structs;
using MVLibraryNET.Definitions.Utilities;

namespace MVLibraryNET.MVGL;

public interface IMvglReader : IDisposable, IAsyncDisposable
{
    MvglFile[] GetFiles();

    ArrayRental ExtractFile(in MvglFile file);

    ArrayRental ExtractFileNoDecompression(in MvglFile file);
}