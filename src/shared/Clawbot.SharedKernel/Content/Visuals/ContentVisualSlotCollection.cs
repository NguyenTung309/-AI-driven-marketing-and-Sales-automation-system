using System.Collections.ObjectModel;

namespace Clawbot.SharedKernel.Content.Visuals;

internal static class ContentVisualSlotCollection
{
    internal static ReadOnlyCollection<ContentVisualSlot> Validate(
        IEnumerable<ContentVisualSlot> slots)
    {
        var suppliedSlots = ContentVisualValidation.CopyBounded(
            slots,
            ContentVisualLimits.MaximumSlotsPerSpec,
            "slot_count_exceeded",
            "$.slots");
        var validatedSlots = new List<ContentVisualSlot>(suppliedSlots.Length);
        var names = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < suppliedSlots.Length; index++)
        {
            var suppliedSlot = suppliedSlots[index];
            var itemPath = $"$.slots[{index}]";
            if (suppliedSlot is null || suppliedSlot.Lines is null)
                throw ContentVisualValidation.Error("slot_invalid", itemPath);

            var validatedSlot = ContentVisualSlot.Create(
                suppliedSlot.Name,
                suppliedSlot.Lines,
                itemPath);
            if (!names.Add(validatedSlot.Name))
            {
                throw ContentVisualValidation.Error(
                    "slot_duplicate",
                    $"$.slots.{validatedSlot.Name}");
            }

            validatedSlots.Add(validatedSlot);
        }

        validatedSlots.Sort((left, right) =>
            StringComparer.Ordinal.Compare(left.Name, right.Name));
        return validatedSlots.AsReadOnly();
    }
}
