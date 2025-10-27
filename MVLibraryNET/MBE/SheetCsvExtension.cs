using System.Text;

namespace MVLibraryNET.MBE;

public static class SheetCsvExtension
{
    public static string ToCsv(this Sheet sheet)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < sheet.ColCodes.Length; i++)
        {
            var col = sheet.ColCodes[i];
            sb.Append(col);

            // Don't append comma after last column.
            if (i == sheet.ColCodes.Length - 1) break;
            sb.Append(',');
        }

        sb.AppendLine();
        
        var currRow = 0;
        foreach (var kvp in sheet.Cells)
        {
            var cell = kvp.Key;
            var value = kvp.Value;
            
            // Cells can be assumed to be in sequential order.
            // Add new line when row changes.
            if (cell.Row != currRow)
            {
                sb.AppendLine();
                currRow = kvp.Key.Row;
            }
            
            var colCode = sheet.ColCodes[cell.Column];
            switch (colCode)
            {
                case ColumnType.Bool:
                    sb.Append(value == 0 ? "false" : "true");
                    break;
                case ColumnType.Int:
                case ColumnType.Short:
                case ColumnType.Byte:
                    sb.Append(value);
                    break;
                case ColumnType.Float:
                    sb.Append((float)value);
                    break;
                case ColumnType.String:
                case ColumnType.String2:
                case ColumnType.String3:
                case ColumnType.Empty:
                    var strValue = $"\"{sheet.GetCellString(ref cell) ?? string.Empty}\"";
                    sb.Append(strValue);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Don't append comma after last column.
            if (cell.Column != sheet.ColCodes.Length - 1)
            {
                sb.Append(',');
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }
}