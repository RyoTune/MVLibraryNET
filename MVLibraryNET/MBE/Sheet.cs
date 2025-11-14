using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using nietras.SeparatedValues;

namespace MVLibraryNET.MBE;

public unsafe class Sheet
{
    private static readonly SepReaderOptions CsvReader = Sep.Reader(_ => new() { Unescape = true, Sep = new(',') });

    private static readonly Dictionary<ColumnType, byte> ColumnSizes = new()
    {
        [ColumnType.Empty] = 0,
        [ColumnType.Bool] = 0, // Should be calculated manually.
        [ColumnType.Byte] = 1,
        [ColumnType.Short] = 2,
        [ColumnType.Int] = 4,
        [ColumnType.Float] = 4,
        [ColumnType.String] = 8,
        [ColumnType.String2] = 8,
        [ColumnType.String3] = 8,
        [ColumnType.IntArray] = 8,
    };
    
    
    internal readonly ColumnType[] ColCodes;
    private readonly long _basePos;
    private readonly int _rowSize;
    private readonly int _initNumRows;
    private readonly Dictionary<Cell, object> _cellsChnkValues = [];
    
    // Would *prefer* to calculate cell offset from base pos + row offset
    // but this is shrimply easier... *sigh*
    private readonly Dictionary<long, Cell> _chnkOffsetToCellMap = [];

    public Sheet(string name, string csvContent)
    {
        Name = name;

        using var csv = CsvReader.FromText(csvContent);
        ColCodes = csv.Header.ColNames.Select(GetColumnType).ToArray();
        _rowSize = ColCodes.Length;

        var rowIdx = 0;
        foreach (var row in csv)
        {
            for (int colIdx = 0; colIdx < row.ColCount; colIdx++)
            {
                var col = row[colIdx];
                var cellValueStr = col.ToString();
                var cell = new Cell(rowIdx, colIdx);
                var colType = ColCodes[colIdx];
                var cellValue = GetCellValue(colType, cellValueStr);
                Cells[cell] = cellValue;

                if (IsColumnChnk(colType))
                    _cellsChnkValues[cell] = GetChnkValue(colType, cellValueStr);
            }

            rowIdx++;
        }
    }

    private static ColumnType GetColumnType(string typeName)
    {
        typeName = typeName.Split(' ', '_').First().ToLowerInvariant();
        if (Enum.TryParse(typeName, true, out ColumnType type) 
            || Enum.TryParse(typeName, true, out type))
        {
            return type;
        }
        
        switch (typeName)
        {
            case "int32": return ColumnType.Int;
            case "int16": return ColumnType.Short;
            case "int8": return ColumnType.Byte;
            case "int array": return ColumnType.IntArray;
        }

        throw new InvalidDataException($"Unknown column type: {typeName}");
    }

    public Sheet(string csvFile) : this(Path.GetFileNameWithoutExtension(csvFile), File.ReadAllText(csvFile)) {}
    
    public Sheet(BinaryReader br, Chnk chnk)
    {
        var stream = br.BaseStream;
        stream.AlignStream(8);
        
        Name = br.ReadStringIncludingLength();
        
        var numCols = br.ReadInt32();
        ColCodes = GC.AllocateUninitializedArray<ColumnType>(numCols);
        fixed (ColumnType* ptr = ColCodes) stream.ReadExactly(new(ptr, sizeof(ColumnType) * numCols));
        
        _rowSize = br.ReadInt32();
        _initNumRows = br.ReadInt32();

        _basePos = stream.Position;
        var rowBuffer = GC.AllocateUninitializedArray<byte>(_rowSize);
        for (var rowIdx = 0; rowIdx < _initNumRows; rowIdx++)
        {
            stream.AlignStream(8);

            var rowPos = stream.Position;
            stream.ReadExactly(new(rowBuffer));
            
            var cellOffset = 0;
            
            var currBools = 0;
            byte currBitOffset = 0;
            
            for (var colIdx = 0; colIdx < numCols; colIdx++)
            {
                var cell = new Cell(rowIdx, colIdx);
                var colCode = ColCodes[colIdx];
                long cellValue = 0;
                
                switch (colCode)
                {
                    case ColumnType.Bool:
                        if (currBitOffset == 0)
                        {
                            Utils.Align(ref cellOffset, 4);
                            currBools = BitConverter.ToInt32(rowBuffer, cellOffset);
                            cellOffset += 4;
                        }

                        cellValue = (currBools >> currBitOffset) & 1;
                        currBitOffset++;
                        if (currBitOffset >= 32) currBitOffset = 0;
                        break;
                    case ColumnType.Empty:
                        break;
                    case ColumnType.Byte:
                        cellValue = (sbyte)rowBuffer[cellOffset];
                        cellOffset += 1;
                        break;
                    case ColumnType.Short:
                        Utils.Align(ref cellOffset, 2);
                        cellValue = BitConverter.ToInt16(rowBuffer, cellOffset);
                        cellOffset += 2;
                        break;
                    case ColumnType.Int:
                        Utils.Align(ref cellOffset, 4);
                        cellValue = BitConverter.ToInt32(rowBuffer, cellOffset);
                        cellOffset += 4;
                        break;
                    case ColumnType.Float:
                        Utils.Align(ref cellOffset, 4);
                        cellValue = Unsafe.BitCast<float, int>((float)Math.Round(BitConverter.ToSingle(rowBuffer, cellOffset), 3));
                        cellOffset += 4;
                        break;
                    case ColumnType.String:
                    case ColumnType.String2:
                    case ColumnType.String3:
                        Utils.Align(ref cellOffset, 8);
                        chnk.SetChnkItem((int)(rowPos + cellOffset), string.Empty);
                        _chnkOffsetToCellMap[rowPos + cellOffset] = cell;
                        cellOffset += 8;
                        break;
                    case ColumnType.IntArray:
                        Utils.Align(ref cellOffset, 8);
                        chnk.SetChnkItem((int)(rowPos + cellOffset), Array.Empty<int>());
                        _chnkOffsetToCellMap[rowPos + cellOffset] = cell;
                        cellOffset += 8;
                        break;
                    default:
                        throw new InvalidDataException($"Unknown column type code: 0x{colCode:X}");
                }
                
                Cells[cell] = cellValue;
            }
        }
    }

    public string Name { get; }

    /// <summary>
    /// Dictionary of cells. Can be assumed to be in sequential order.
    /// </summary>
    public Dictionary<Cell, long> Cells { get; } = [];
    
    /// <summary>
    /// Writes sheet data to stream.
    /// </summary>
    /// <param name="bw"><see cref="BinaryWriter"/> to use.</param>
    /// <param name="chnk">Chnk instance.</param>
    /// <returns>Position to start of row data (needed for writing string map).</returns>
    public void Write(BinaryWriter bw, Chnk chnk)
    {
        var stream = bw.BaseStream;
        stream.AlignStream(8);

        bw.WriteStringIncludingLength(Name);

        bw.Write(ColCodes.Length);
        fixed (ColumnType* ptr = ColCodes)
            stream.Write(new(ptr, sizeof(ColumnType) * ColCodes.Length));

        // Cells are sequential, last cell row idx is the same as the total rows.
        var numRows = GetNumRows();
        bw.Write(_rowSize);
        bw.Write(numRows);

        for (var rowIdx = 0; rowIdx < numRows; rowIdx++)
        {
            stream.AlignStream(8);
            var rowPos = stream.Position;
            var cellOffset = 0;
            var rowBuffer = GC.AllocateUninitializedArray<byte>(_rowSize);

            int currBools = 0;
            byte currBitOffset = 0;
            int boolWritePos = -1;

            for (var colIdx = 0; colIdx < ColCodes.Length; colIdx++)
            {
                var cell = new Cell(rowIdx, colIdx);
                var colCode = ColCodes[colIdx];
                var cellValue = Cells[cell];

                switch (colCode)
                {
                    case ColumnType.Bool:
                        if (currBitOffset == 0)
                        {
                            Utils.Align(ref cellOffset, 4);
                            boolWritePos = cellOffset;
                            cellOffset += 4;
                            currBools = 0;
                        }

                        if (cellValue != 0)
                            currBools |= 1 << currBitOffset;

                        currBitOffset++;
                        if (currBitOffset >= 32)
                        {
                            BitConverter.TryWriteBytes(rowBuffer.AsSpan(boolWritePos), currBools);
                            currBitOffset = 0;
                            boolWritePos = -1;
                        }
                        break;

                    case ColumnType.Empty:
                        break;

                    case ColumnType.Byte:
                        rowBuffer[cellOffset++] = (byte)cellValue;
                        break;

                    case ColumnType.Short:
                        Utils.Align(ref cellOffset, 2);
                        BitConverter.TryWriteBytes(rowBuffer.AsSpan(cellOffset), (short)cellValue);
                        cellOffset += 2;
                        break;

                    case ColumnType.Int:
                        Utils.Align(ref cellOffset, 4);
                        BitConverter.TryWriteBytes(rowBuffer.AsSpan(cellOffset), (int)cellValue);
                        cellOffset += 4;
                        break;
                    case ColumnType.Float:
                        Utils.Align(ref cellOffset, 4);
                        var fValue = (float)Math.Round(Unsafe.BitCast<int, float>((int)cellValue), 3);
                        BitConverter.TryWriteBytes(rowBuffer.AsSpan(cellOffset), fValue);
                        cellOffset += 4;
                        break;

                    case ColumnType.String:
                    case ColumnType.String2:
                    case ColumnType.String3:
                    case ColumnType.IntArray:
                        Utils.Align(ref cellOffset, 8);
                        BitConverter.TryWriteBytes(rowBuffer.AsSpan(cellOffset), 0L);

                        if (_cellsChnkValues.TryGetValue(cell, out var chnkValue))
                            chnk.SetChnkItem((int)(rowPos + cellOffset), chnkValue);
                        
                        cellOffset += 8;
                        break;

                    default:
                        throw new InvalidDataException($"Unknown column type code: 0x{colCode:X}");
                }
            }

            // Write any leftover bools (if the row ends mid-block)
            if (currBitOffset > 0 && boolWritePos >= 0)
                BitConverter.TryWriteBytes(rowBuffer.AsSpan(boolWritePos), currBools);

            stream.Write(rowBuffer);
        }
    }
    
    public void MergeDiff(SheetDiff diff)
    {
        foreach (var kvp in diff.Cells) Cells[kvp.Key] = kvp.Value;
        foreach (var kvp in diff.ChnkCells) _cellsChnkValues[kvp.Key] = kvp.Value;
    }

    /// <summary>
    /// Appends CSV data to sheet.
    /// </summary>
    /// <param name="csvContent">CSV row string.</param>
    public void AppendCsv(string csvContent)
    {
        var csv = CsvReader.FromText(csvContent);

        var rowIdx = GetNumRows();
        foreach (var row in csv)
        {
            for (var colIdx = 0; colIdx < row.ColCount; colIdx++)
            {
                var col = row[colIdx];
                var cellValueStr = col.ToString();
                var cell = new Cell(rowIdx, colIdx);
                var colType = ColCodes[colIdx];
                var cellValue = GetCellValue(colType, cellValueStr);
                Cells[cell] = cellValue;

                if (IsColumnChnk(colType))
                    _cellsChnkValues[cell] = GetChnkValue(colType, cellValueStr);
            }

            rowIdx++;
        }
    }

    public record SheetDiff(IReadOnlyDictionary<Cell, long> Cells, IReadOnlyDictionary<Cell, object> ChnkCells);
    
    /// <summary>
    /// Generates a diff of cells and strings compared to <paramref name="other"/>.<br/>
    /// Ideal use case is for diffing against original sheet, then merging into an active sheet.
    /// </summary>
    /// <param name="other">Other sheet to compare to.</param>
    public SheetDiff GenerateDiff(Sheet other)
    {
        var diffCells = other.Cells.Where(x => IsCellDiff(x.Key, x.Value)).ToDictionary();
        var diffStrings = other._cellsChnkValues.Where(x => IsCellChnkDiff(x.Key, x.Value)).ToDictionary();
        return new(diffCells, diffStrings);
    }

    private bool IsCellDiff(Cell otherCell, long value)
    {
        if (!Cells.TryGetValue(otherCell, out var cellValue)) return true;
        return cellValue != value;
    }

    private bool IsCellChnkDiff(Cell otherCell, object otherValue)
    {
        if (!_cellsChnkValues.TryGetValue(otherCell, out var currValue)) return true;
        
        if (currValue is int[] currInts)
        {
            return currInts.SequenceEqual((int[])otherValue);
        }
        
        return !currValue.Equals(otherValue);
    }

    /// <summary>
    /// Applies an MBE's Chnk to Cell values.
    /// </summary>
    /// <param name="chnk">MBE Chnk section.</param>
    public void ApplyMbeChnk(Chnk chnk)
    {
        foreach (var kvp in _chnkOffsetToCellMap)
        {
            if (chnk.TryGetChnkItem((int)kvp.Key, out var chnkValue))
            {
                _cellsChnkValues[kvp.Value] = chnkValue;
            }
        }
    }

    private static bool IsColumnChnk(ColumnType type) =>
        type is ColumnType.String or ColumnType.String2 or ColumnType.String3 or ColumnType.IntArray;

    private static object GetChnkValue(ColumnType type, string chnkValueStr)
    {
        if (type == ColumnType.IntArray)
        {
            return chnkValueStr
                .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse)
                .ToArray();
        }

        return chnkValueStr;
    }

    public bool TryGetCellChnkValue(Cell cell, [NotNullWhen(true)] out object? value)
    {
        _cellsChnkValues.TryGetValue(cell, out value);
        return value != null;
    }

    private static long GetCellValue(ColumnType type, string valueStr)
    {
        var valOrZero = ValueOrZero(valueStr);
        return type switch
        {
            ColumnType.Int => int.Parse(valOrZero),
            ColumnType.Short => short.Parse(valOrZero),
            ColumnType.Byte => sbyte.Parse(valOrZero),
            ColumnType.Float => Unsafe.BitCast<float, int>((float)Math.Round(float.Parse(valOrZero), 3)),
            ColumnType.String3 or ColumnType.String or ColumnType.String2 => 0,
            ColumnType.Bool => char.IsDigit(valOrZero.First()) ? valOrZero == "1" ? 1 : 0 : bool.Parse(valueStr) ? 1 : 0,
            ColumnType.Empty => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    private static string ValueOrZero(string value) => string.IsNullOrEmpty(value) ? "0" : value;

    private int GetCellRowOffset(ref Cell cell)
    {
        var cellRowOfs = 0;
        var currBitOffset = 0;
        for (var i = 0; i < ColCodes.Length; i++)
        {
            var colType = ColCodes[i];
            if (colType == ColumnType.Empty) continue;
            
            if (colType == ColumnType.Bool)
            {
                if (currBitOffset == 0)
                {
                    Utils.Align(ref cellRowOfs, 4);
                    cellRowOfs += 4;
                }
                
                currBitOffset++;
                if (currBitOffset >= 32) currBitOffset = 0;
                
                if (i == cell.Column) break;
            }
            else
            {
                var colSize = ColumnSizes[colType];
                Utils.Align(ref cellRowOfs, colSize);

                if (i == cell.Column) break;
                cellRowOfs += colSize;
            }
        }

        return cellRowOfs;
    }

    private Cell GetCellByOffset(long offset)
    {
        var rowIdx = (offset - _basePos) / _rowSize;
        var colIdx = GetColByRowOffset((offset - _basePos) % _rowSize);
        return new((int)rowIdx, (int)colIdx);
    }

    private int GetColByRowOffset(long targetRowOffset)
    {
        var cellRowOfs = 0;
        var currBitOffset = 0;
        for (var i = 0; i < ColCodes.Length; i++)
        {
            var colType = ColCodes[i];
            if (colType == ColumnType.Empty) continue;
            
            if (colType == ColumnType.Bool)
            {
                if (currBitOffset == 0)
                {
                    Utils.Align(ref cellRowOfs, 4);
                    cellRowOfs += 4;
                }
                
                currBitOffset++;
                if (currBitOffset >= 32) currBitOffset = 0;

                if (cellRowOfs == targetRowOffset) return i;
            }
            else
            {
                var colSize = ColumnSizes[colType];
                Utils.Align(ref cellRowOfs, colSize);

                if (cellRowOfs == targetRowOffset) return i;
                cellRowOfs += colSize;
            }
        }

        return ColCodes.Length - 1;
    }

    /// <summary>
    /// Gets the cell's offset within the sheet.
    /// </summary>
    internal long GetCellOffset(ref Cell cell) => cell.Row * _rowSize + GetCellRowOffset(ref cell);

    /// <summary>
    /// Gets the row size, aligned to 8.<br/>
    /// Not completely accurate to actual files for who knows why...
    /// </summary>
    /// <returns></returns>
    private int GetRowSize()
    {
        var rowSize = 0;
        var currBitOffset = 0;
        foreach (var colCode in ColCodes)
        {
            var colSize = ColumnSizes[colCode];
            if (colCode == ColumnType.Bool)
            {
                if (currBitOffset == 0)
                {
                    Utils.Align(ref rowSize, 4);
                    rowSize += 4;
                }

                currBitOffset++;
                if (currBitOffset >= 32) currBitOffset = 0;
            }
            else
            {
                Utils.Align(ref rowSize, colSize);
                rowSize += colSize;
            }
        }
    
        return Utils.Align8(rowSize);
    }

    private int GetNumRows() => Cells.Count > 0 ? Cells.Last().Key.Row + 1 : 0;
}
