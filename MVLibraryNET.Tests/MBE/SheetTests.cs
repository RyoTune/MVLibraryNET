using MVLibraryNET.MBE;

namespace MVLibraryNET.Tests.MBE;

public class SheetTests
{
    [Fact]
    public void Sheet_LoadFromCsv_Works()
    {
        var sheet = new Sheet("./Data/Format2.csv");
    }
    
    [Fact]
    public void Sheet_GenerateDiff_Works()
    {
        var og = new Sheet("./Data/Format.csv");
        var other = new Sheet("./Data/Format2.csv");

        var diff = og.GenerateDiff(other);
        
        Assert.Equal(3, diff.Cells.Count);
        Assert.Equal(3, diff.Strings.Count);
    }

    [Fact]
    public void Sheet_MergeDiff_Works()
    {
        var og = new Sheet("./Data/Format.csv");
        var other = new Sheet("./Data/Format2.csv");

        var diff = og.GenerateDiff(other);
        og.MergeDiff(diff);

        foreach (var cell in og.Cells)
        {
            Assert.Equal(cell.Value, other.Cells[cell.Key]);
        }
    }

    [Fact]
    public void Sheet_ToFromCsv_Works()
    {
        var og = new Sheet("./Data/Format.csv");
        var csv = og.ToCsv();
        var other = new Sheet(og.Name, csv);
        
        foreach (var cell in og.Cells)
        {
            Assert.Equal(cell.Value, other.Cells[cell.Key]);
        }
    }
    
    [Fact]
    public void Sheet_DiffMergeToCsvEqual_Works()
    {
        var og = new Sheet("./Data/Format.csv");
        var other = new Sheet("./Data/Format2.csv");
        var diff = og.GenerateDiff(other);
        og.MergeDiff(diff);
        
        var csv = og.ToCsv();
        var otherCsv = other.ToCsv();
        
        Assert.Equal(csv, otherCsv);
    }
}