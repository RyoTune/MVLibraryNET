namespace MVLibraryNET.MBE;

public unsafe class Sheet
{
    private const int NumBoolBits = 32;

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
    };
    
    internal readonly ColumnType[] ColCodes = [];
    
    private readonly int _rowSize;
    private readonly long _basePos;
    private int _numRows;

    public Sheet(string name, string csvContent)
    {
        Name = name;
        var lines = csvContent.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        _numRows = lines.Length - 1;
        for (var rowIdx = 0; rowIdx < lines.Length; rowIdx++)
        {
            var rowStr = lines[rowIdx];
            var rowCells = rowStr.Split(',');
            
            // Load header row column types.
            if (rowIdx == 0)
            {
                ColCodes = rowCells.Select(x => Enum.Parse<ColumnType>(x.Split(' ').First(), true)).ToArray();
                _rowSize = GetRowSize();
                continue;
            }
            
            for (var colIdx = 0; colIdx < rowCells.Length; colIdx++)
            {
                var cell = new Cell(rowIdx - 1, colIdx);
                var colType = ColCodes[colIdx];
                var cellValueStr = rowCells[colIdx];
                var cellValue = GetCellValue(colType, cellValueStr);
                Cells[cell] = cellValue;

                if (IsColumnString(colType))
                    SetCellString(ref cell, cellValueStr.Trim('"'));
            }
        }
    }

    public Sheet(string csvFile) : this(Path.GetFileNameWithoutExtension(csvFile), File.ReadAllText(csvFile)) {}
    
    public Sheet(BinaryReader br)
    {
        var stream = br.BaseStream;
        stream.AlignStream(8);
        
        Name = br.ReadStringIncludingLength();
        
        var numCols = br.ReadInt32();
        ColCodes = GC.AllocateUninitializedArray<ColumnType>(numCols);
        fixed (ColumnType* ptr = ColCodes) stream.ReadExactly(new(ptr, sizeof(ColumnType) * numCols));
        
        _rowSize = br.ReadInt32();
        #if DEBUG
        var calcRowSize = GetRowSize();
        if (calcRowSize < _rowSize) throw new();
        #endif
        _numRows = br.ReadInt32();

        _basePos = stream.Position;
        var rowBuffer = GC.AllocateUninitializedArray<byte>(_rowSize);
        for (var rowIdx = 0; rowIdx < _numRows; rowIdx++)
        {
            stream.AlignStream(8);

            var rowOffset = stream.Position;
            stream.ReadExactly(new(rowBuffer));
            
            var rowCellOffset = 0;
            
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
                            Utils.Align(ref rowCellOffset, 4);
                            currBools = BitConverter.ToInt32(rowBuffer, rowCellOffset);
                            rowCellOffset += 4;
                        }

                        cellValue = (currBools >> currBitOffset) & 1;
                        currBitOffset++;
                        if (currBitOffset >= 32) currBitOffset = 0;
                        break;
                    case ColumnType.Empty:
                        break;
                    case ColumnType.Byte:
                        cellValue = rowBuffer[rowCellOffset];
                        rowCellOffset += 1;
                        break;
                    case ColumnType.Short:
                        Utils.Align(ref rowCellOffset, 2);
                        cellValue = BitConverter.ToInt16(rowBuffer, rowCellOffset);
                        rowCellOffset += 2;
                        break;
                    case ColumnType.Int:
                        Utils.Align(ref rowCellOffset, 4);
                        cellValue = BitConverter.ToInt32(rowBuffer, rowCellOffset);
                        rowCellOffset += 4;
                        break;
                    case ColumnType.Float:
                        Utils.Align(ref rowCellOffset, 4);
                        cellValue = BitConverter.ToInt32(rowBuffer, rowCellOffset);
                        rowCellOffset += 4;
                        break;
                    case ColumnType.String:
                    case ColumnType.String2:
                    case ColumnType.String3:
                        Utils.Align(ref rowCellOffset, 8);
                        SetCellString(ref cell, null);
                        rowCellOffset += 8;
                        break;
                    default:
                        throw new InvalidDataException($"Unknown column type code: 0x{colCode:X}");
                }
                
                Cells[cell] = cellValue;
            }
        }
    }

    private void SetCellString(ref Cell cell, string? str)
    {
        var strOfs = GetCellRowOffset(ref cell) + cell.Row * _rowSize;
        RelativeStringMap[strOfs] = str;
    }

    public string? GetCellString(ref Cell cell)
    {
        var strOfs = GetCellRowOffset(ref cell) + cell.Row * _rowSize;
        RelativeStringMap.TryGetValue(strOfs, out var str);
        return str;
    }

    public string Name { get; }

    /// <summary>
    /// Dictionary of cells. Can be assumed to be in sequential order.
    /// </summary>
    public Dictionary<Cell, long> Cells { get; } = [];

    /// <summary>
    /// String map, with the key being a cell's position relative to the start of row data.
    /// </summary>
    public Dictionary<long, string?> RelativeStringMap { get; } = [];

    /// <summary>
    /// Writes sheet data to stream.
    /// </summary>
    /// <param name="bw"><see cref="BinaryWriter"/> to use.</param>
    /// <returns>Position to start of row data (needed for writing string map).</returns>
    public int Write(BinaryWriter bw)
    {
        var stream = bw.BaseStream;
        stream.AlignStream(8);

        bw.WritePaddedStringIncludingLength(Name);

        bw.Write(ColCodes.Length);
        fixed (ColumnType* ptr = ColCodes)
            stream.Write(new(ptr, sizeof(ColumnType) * ColCodes.Length));

        bw.Write(_rowSize);
        bw.Write(_numRows);

        var rowsPos = stream.Position;
        for (var rowIdx = 0; rowIdx < _numRows; rowIdx++)
        {
            stream.AlignStream(8);
            var rowBuffer = GC.AllocateUninitializedArray<byte>(_rowSize);
            var rowCellOffset = 0;

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
                            Utils.Align(ref rowCellOffset, 4);
                            boolWritePos = rowCellOffset;
                            rowCellOffset += 4;
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
                        rowBuffer[rowCellOffset++] = (byte)cellValue;
                        break;

                    case ColumnType.Short:
                        Utils.Align(ref rowCellOffset, 2);
                        BitConverter.TryWriteBytes(rowBuffer.AsSpan(rowCellOffset), (short)cellValue);
                        rowCellOffset += 2;
                        break;

                    case ColumnType.Int:
                    case ColumnType.Float:
                        Utils.Align(ref rowCellOffset, 4);
                        BitConverter.TryWriteBytes(rowBuffer.AsSpan(rowCellOffset), (int)cellValue);
                        rowCellOffset += 4;
                        break;

                    case ColumnType.String:
                    case ColumnType.String2:
                    case ColumnType.String3:
                        Utils.Align(ref rowCellOffset, 8);
                        // Presumably you’ll actually look up and write the string pointer or ID here.
                        BitConverter.TryWriteBytes(rowBuffer.AsSpan(rowCellOffset), 0L);
                        rowCellOffset += 8;
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

        return (int)rowsPos;
    }
    
    public void MergeDiff(SheetDiff diff)
    {
        foreach (var cell in diff.Cells) Cells[cell.Key] = cell.Value;
        foreach (var str in diff.Strings) RelativeStringMap[str.Key] = str.Value;
    }

    /// <summary>
    /// Appends a new row from a CSV row string.
    /// </summary>
    /// <param name="row">CSV row string.</param>
    public void AppendRow(string row)
    {
        var rowCells = row.Split(',');
        for (var colIdx = 0; colIdx < rowCells.Length; colIdx++)
        {
            var cell = new Cell(_numRows, colIdx);
            var colType = ColCodes[colIdx];
            var cellValueStr = rowCells[colIdx];
            var cellValue = GetCellValue(colType, cellValueStr);
            Cells[cell] = cellValue;

            if (IsColumnString(colType))
                SetCellString(ref cell, cellValueStr.Trim('"'));
        }

        _numRows++;
    }

    public record SheetDiff(IReadOnlyDictionary<Cell, long> Cells, IReadOnlyDictionary<long, string?> Strings);
    
    /// <summary>
    /// Generates a diff of cells and strings compared to <paramref name="other"/>.<br/>
    /// Ideal use case is for diffing against original sheet, then merging into an active sheet.
    /// </summary>
    /// <param name="other">Other sheet to compare to.</param>
    public SheetDiff GenerateDiff(Sheet other)
    {
        var diffCells = other.Cells.Where(x => IsCellDiff(x.Key, x.Value)).ToDictionary();
        var diffStrings = other.RelativeStringMap.Where(x => IsStringDiff(x.Key, x.Value)).ToDictionary();
        return new(diffCells, diffStrings);
    }

    private bool IsCellDiff(Cell otherCell, long value)
    {
        if (!Cells.TryGetValue(otherCell, out var cellValue)) return true;
        return cellValue != value;
    }

    private bool IsStringDiff(long cellOfs, string? otherStr)
    {
        if (!RelativeStringMap.TryGetValue(cellOfs, out var currStr)) return true;
        return currStr != otherStr;
    }

    /// <summary>
    /// Applies an MBE's string map to any string cells.
    /// </summary>
    /// <param name="offsetToStringMap">MBE string map, which uses absolute cell positions (offset from beginning of file) for keys.</param>
    public void ApplyMbeStringMap(Dictionary<long, string> offsetToStringMap)
    {
        foreach (var offset in RelativeStringMap)
        {
            if (offsetToStringMap.TryGetValue(Utils.Align8((int)(offset.Key + _basePos)), out var str))
                RelativeStringMap[offset.Key] = str;
        }
    }

    private static bool IsColumnString(ColumnType type) =>
        type is ColumnType.String or ColumnType.String2 or ColumnType.String3;

    private static long GetCellValue(ColumnType type, string valueStr) =>
        type switch
        {
            ColumnType.Int => int.Parse(valueStr),
            ColumnType.Short => short.Parse(valueStr),
            ColumnType.Byte => sbyte.Parse(valueStr),
            ColumnType.Float => (long)float.Parse(valueStr),
            ColumnType.String3 or ColumnType.String or ColumnType.String2 => 0,
            ColumnType.Bool => bool.Parse(valueStr) ? 1 : 0,
            ColumnType.Empty => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

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
}