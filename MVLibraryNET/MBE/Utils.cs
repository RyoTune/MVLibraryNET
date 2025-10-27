using System.Text;

namespace MVLibraryNET.MBE;

public static class Utils
{
    public static string ReadStringIncludingLength(this BinaryReader br)
    {
        var len = br.ReadInt32();
        if (len == 0) return string.Empty;
        
        var strSpan = new Span<byte>(br.ReadBytes(len));
        if (strSpan[^1] == 0)
        {
            strSpan = strSpan.Slice(0, strSpan.IndexOf((byte)0));
        }

        return Encoding.UTF8.GetString(strSpan);
    }
    
    public static void WritePaddedStringIncludingLength(this BinaryWriter bw, string str)
    {
        str += "\0\0";
        var alignedLen = Align4(str.Length);
        str = str.PadRight(alignedLen, '\0');
        
        bw.Write(str.Length);
        bw.Write(Encoding.UTF8.GetBytes(str));
    }

    public static void AlignStream(this Stream stream, byte alignment) =>
        stream.Position = alignment switch
        {
            8 => Align8((int)stream.Position),
            4 => Align4((int)stream.Position),
            2 => Align2((int)stream.Position),
            _ => throw new InvalidOperationException()
        };

    public static void Align(ref int offset, byte alignment)
    {
        switch (alignment)
        {
            case 16: offset = Align16(offset); break;
            case 8: offset = Align8(offset); break;
            case 4: offset = Align4(offset); break;
            case 2: offset = Align2(offset); break;
            default: return;
        }
    }
    
    public static int Align16(int offset) => (offset + 15) & ~15;
    
    public static int Align8(int offset) => (offset + 7) & ~7;
    
    public static int Align4(int offset) => (offset + 3) & ~3;
    
    public static int Align2(int offset) => (offset + 1) & ~1;
    
    public static string TrimOneQuote(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            return s.Substring(1, s.Length - 2);
        if (s.Length > 0 && s[0] == '"')
            return s[1..];
        if (s.Length > 0 && s[^1] == '"')
            return s[..^1];
        return s;
    }
}