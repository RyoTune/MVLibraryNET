using System.Text;

namespace MVLibraryNET.MBE;

public class Mbe
{
    private const int Expa = 0x41505845;
    private const int Chnk = 0x4B4E4843;
    private readonly Dictionary<long, string> _stringMap = [];
    
    public Mbe(string mbeFile) : this(File.OpenRead(mbeFile), true)
    {
    }

    public Mbe(Stream stream, bool ownsStream)
    {
        using var br = new BinaryReader(stream, Encoding.Default, !ownsStream);
        
        // ReSharper disable once InconsistentNaming
        if (br.ReadInt32() != Expa) throw new InvalidDataException("'EXPA' not found.");

        // Load sheets.
        var numSheets = br.ReadInt32();
        for (int i = 0; i < numSheets; i++)
        {
            var sheet = new Sheet(br);
            Sheets[sheet.Name] = sheet;
        }
        
        // Build string maps.
        br.BaseStream.AlignStream(8);
        if (br.ReadInt32() == Chnk)
        {
            var numStrings = br.ReadInt32();
            for (var i = 0; i < numStrings; i++)
            {
                var cellPos = br.ReadInt32();
                var str = br.ReadStringIncludingLength();
                _stringMap[cellPos] = str;
            }
        }
        
        // Fix up string cells.
        foreach (var sheet in Sheets.Values) sheet.ApplyMbeStringMap(_stringMap);
    }

    public Dictionary<string, Sheet> Sheets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Write(Stream stream)
    {
        var bw = new BinaryWriter(stream);
        
        bw.Write(Expa);
        bw.Write(Sheets.Count);

        var strMap = new Dictionary<long, string>();
        foreach (var sheet in Sheets.Values)
        {
            var rowsPos = sheet.Write(bw);
            foreach (var kvp in sheet.StringMap.Where(x => !string.IsNullOrEmpty(x.Value)))
            {
                var cell = kvp.Key;
                var offset = Utils.Align8((int)(sheet.GetCellOffset(ref cell) + rowsPos));
                strMap[offset] = kvp.Value;
            }
        }

        bw.BaseStream.AlignStream(8);
        if (strMap.Count <= 0)
        {
            bw.Write(0);
            return;
        }
        
        bw.Write(Chnk);
        bw.Write(strMap.Count);
        foreach (var kvp in strMap)
        {
            bw.Write(Utils.Align8((int)kvp.Key));
            bw.WritePaddedStringIncludingLength(kvp.Value);
        }
    }
}