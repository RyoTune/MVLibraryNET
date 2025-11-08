using System.Text;

namespace MVLibraryNET.MBE;

public class Mbe
{
    private const int Expa = 0x41505845;
    
    public Mbe(string mbeFile) : this(File.OpenRead(mbeFile), true)
    {
    }

    public Mbe(Stream stream, bool ownsStream)
    {
        using var br = new BinaryReader(stream, Encoding.Default, !ownsStream);
        
        // ReSharper disable once InconsistentNaming
        if (br.ReadInt32() != Expa) throw new InvalidDataException("'EXPA' not found.");

        // Load sheets.
        var chnk = new Chnk();
        var numSheets = br.ReadInt32();
        for (int i = 0; i < numSheets; i++)
        {
            var sheet = new Sheet(br, chnk);
            Sheets[sheet.Name] = sheet;
        }
        
        // Build string maps.
        br.BaseStream.AlignStream(8);
        if (br.ReadInt32() == Chnk.Magic) chnk.Read(br);
        
        // Fix up string cells.
        foreach (var sheet in Sheets.Values) sheet.ApplyMbeChnk(chnk);
    }

    public Dictionary<string, Sheet> Sheets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Write(Stream stream)
    {
        var bw = new BinaryWriter(stream);
        var chnk = new Chnk();
        
        bw.Write(Expa);
        bw.Write(Sheets.Count);

        foreach (var sheet in Sheets.Values)
        {
            sheet.Write(bw, chnk);
        }

        bw.BaseStream.AlignStream(8);
        chnk.Write(bw);
    }
}