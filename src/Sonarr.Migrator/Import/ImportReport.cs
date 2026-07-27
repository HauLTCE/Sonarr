using System.Text;

namespace Sonarr.Migrator.Import;

/// <summary>
/// Row-count reconciliation. The checklist requires the import to print a table you can eyeball
/// against the old data, so every step reports read/written/skipped instead of just "done".
/// </summary>
public sealed class ImportReport
{
    private readonly List<Row> _rows = [];

    public void Add(string step, int read, int written, int skipped, string? note = null) =>
        _rows.Add(new Row(step, read, written, skipped, note));

    public void AddSkipped(string step, string why) =>
        _rows.Add(new Row(step, 0, 0, 0, why));

    public int TotalWritten => _rows.Sum(r => r.Written);

    public override string ToString()
    {
        int stepWidth = Math.Max(24, _rows.Count == 0 ? 0 : _rows.Max(r => r.Step.Length));
        StringBuilder sb = new();
        sb.Append("  ").Append("step".PadRight(stepWidth))
          .Append("   read  written  skipped  note").AppendLine();
        sb.Append("  ").Append(new string('-', stepWidth + 32)).AppendLine();

        foreach (Row row in _rows)
        {
            sb.Append("  ").Append(row.Step.PadRight(stepWidth))
              .Append(row.Read.ToString().PadLeft(7))
              .Append(row.Written.ToString().PadLeft(9))
              .Append(row.Skipped.ToString().PadLeft(9))
              .Append("  ").Append(row.Note ?? string.Empty)
              .AppendLine();
        }

        return sb.ToString();
    }

    private sealed record Row(string Step, int Read, int Written, int Skipped, string? Note);
}
