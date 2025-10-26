using MVLibraryNET.MBE;

namespace MVLibraryNET.Tests.MBE;

public class MbeTests
{
    [Fact]
    public void Mbe_LoadMbeFile_Works()
    {
        _ = new Mbe("./Data/digimon_status.mbe");
    }

    [Fact]
    public void Mbe_Write_Works()
    {
        var ogMbe = new Mbe("./Data/digimon_status.mbe");
        
        var ms = new MemoryStream();
        ogMbe.Write(ms);
        ms.Position = 0;
        var msMbe = new Mbe(ms, true);
        
        foreach (var sheetKvp in ogMbe.Sheets)
        {
            foreach (var cellKvp in sheetKvp.Value.Cells)
            {
                Assert.Equal(cellKvp.Value, msMbe.Sheets[sheetKvp.Key].Cells[cellKvp.Key]);
            }
        }
    }

    [Fact]
    public void Mbe_Test()
    {
        var mbe = new Mbe("./Data/bgm_name.mbe");
        using var outputFs = File.Create("./bgm_name.mbe");
        var csv = mbe.Sheets.First().Value.ToCsv();
        mbe.Write(outputFs);
        outputFs.Dispose();
        var outputMbe = new Mbe("./bgm_name.mbe");
    }
}