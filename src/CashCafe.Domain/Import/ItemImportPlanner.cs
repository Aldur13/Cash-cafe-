namespace CashCafe.Domain.Import;

/// <summary>
/// Decides what an item spreadsheet would do to the menu, without writing anything. Matches
/// rows to existing items by name only — items do not have the fuzzy typo-tolerant matching
/// students get, because a menu is short enough that a person can just look at it, and an
/// exact match is safer than a guess when the outcome is a price silently changing.
/// </summary>
public static class ItemImportPlanner
{
    public static ItemImportPreview Plan(
        IReadOnlyList<ParsedItemRow> rows,
        IReadOnlyList<Item> existingItems,
        string fileName,
        string? sheetName)
    {
        var byName = existingItems
            .Where(i => !i.IsArchived)
            .GroupBy(i => SearchNormalizer.Normalize(i.Name))
            .ToDictionary(g => g.Key, g => g.First());

        var seenInFile = new Dictionary<string, int>();
        var result = new List<ItemImportRow>();

        foreach (var row in rows)
        {
            if (row.Error is not null)
            {
                result.Add(new ItemImportRow
                {
                    Source = row,
                    Status = ItemImportStatus.Error,
                    Action = ItemImportAction.Error,
                    Note = row.Error,
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.Name))
            {
                result.Add(new ItemImportRow
                {
                    Source = row,
                    Status = ItemImportStatus.SkippedNoName,
                    Action = ItemImportAction.Ignore,
                    Note = "Blank row.",
                });
                continue;
            }

            if (row.Price is null)
            {
                result.Add(new ItemImportRow
                {
                    Source = row,
                    Status = ItemImportStatus.Error,
                    Action = ItemImportAction.Error,
                    Note = "No price could be read for this row.",
                });
                continue;
            }

            var normalized = SearchNormalizer.Normalize(row.Name);

            if (seenInFile.TryGetValue(normalized, out var firstRow))
            {
                result.Add(new ItemImportRow
                {
                    Source = row,
                    Status = ItemImportStatus.DuplicateInFile,
                    Action = ItemImportAction.Ignore,
                    Note = $"The same name is already on row {firstRow}",
                });
                continue;
            }

            seenInFile[normalized] = row.RowNumber;

            if (byName.TryGetValue(normalized, out var existing))
            {
                var changed =
                    row.Price != existing.Price ||
                    !string.Equals(row.Category ?? string.Empty, existing.Category ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase) ||
                    (row.Available ?? existing.IsAvailable) != existing.IsAvailable;

                result.Add(new ItemImportRow
                {
                    Source = row,
                    Status = changed ? ItemImportStatus.ExistsDifferent : ItemImportStatus.ExistsUnchanged,
                    Action = changed ? ItemImportAction.Update : ItemImportAction.KeepExisting,
                    MatchedItemId = existing.Id,
                    MatchedItemName = existing.Name,
                    ExistingPrice = existing.Price,
                    Note = changed ? $"Currently {existing.Price}" : null,
                });
                continue;
            }

            result.Add(new ItemImportRow
            {
                Source = row,
                Status = ItemImportStatus.NewItem,
                Action = ItemImportAction.CreateItem,
            });
        }

        return new ItemImportPreview { Rows = result, FileName = fileName, SheetName = sheetName };
    }
}
