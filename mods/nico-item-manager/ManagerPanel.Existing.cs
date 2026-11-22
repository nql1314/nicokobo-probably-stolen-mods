using System;
using System.Collections.Generic;

namespace NicoItemManager
{
    internal sealed partial class ManagerPanel
    {
        string deleteAmount = "1";
        string copyAmount = "1";
        SpawnDestination copyDestination = SpawnDestination.DesktopFirst;
        int? pendingDelete;

        internal void OpenExistingItem(GameItem item)
        {
            try
            {
                if (!IsVisible) Toggle();
                if (!IsVisible) return;
                var next = new ItemEditor(item);
                editor?.Dispose();
                editor = next;
                searchAt = 0;
                selected = new CatalogEntry { Id = item.identifier };
                view = View.Details;
                pendingDelete = null;
                deleteAmount = "1";
                copyAmount = "1";
                newKey = newValue = "";
                newBool = false;
                editorLayoutDirty = previewNeedsRefresh = true;
                SetStatus("");
                RequestRebuild();
            }
            catch (Exception error)
            {
                NicoItemManagerMod.Log?.Warning("Open existing item: " + error);
                SetStatus(error.Message);
            }
        }

        void BuildExistingActions(CustomUIBuilder builder)
        {
            if (pendingDelete.HasValue)
            {
                builder.AddLabel(Text.Get("permanently_delete_unapplied_edits_will_be_discarded", editor.Target.GetDisplayName(), pendingDelete.Value));
                Label(builder.Last, 18, true);
                builder.BeginRow(8);
                builder.AddButton(Text.Get("confirm_deletion"), DeleteExistingItem);
                builder.Last.SetFixedWidth(180);
                builder.AddButton(Text.Get("cancel_deletion"), () =>
                {
                    pendingDelete = null;
                    editorLayoutDirty = true;
                    RequestRebuild();
                });
                builder.Last.SetFixedWidth(180);
                builder.End();
                return;
            }
            builder.BeginRow(8);
            builder.AddLabel(Text.Get("copy_quantity"));
            builder.Last.SetFixedWidth(140);
            AddTextInput(builder, "1", copyAmount, value => copyAmount = value, onTyping: value => copyAmount = value);
            builder.Last.SetFixedWidth(120);
            builder.AddButton(Text.Get("copy_quantity_2"), () => CopyExistingItem(false));
            builder.Last.SetFixedWidth(210);
            builder.AddButton(Text.Get("copy_stack"), () => CopyExistingItem(true));
            builder.Last.SetFixedWidth(180);
            builder.AddLabel(Text.Get("copy_to"));
            builder.Last.SetFixedWidth(110);
            builder.AddDropdown(new List<string> { Text.Get("desktop_first"), Text.Get("backpack") },
                (int)copyDestination, index => copyDestination = (SpawnDestination)index, id: "copy-destination");
            builder.Last.SetFixedWidth(190);
            builder.End();
            builder.BeginRow(8);
            builder.AddButton(Text.Get("apply_changes"), ApplyExistingItem);
            builder.Last.SetFixedWidth(180);
            builder.AddLabel(Text.Get("delete_quantity"));
            builder.Last.SetFixedWidth(140);
            AddTextInput(builder, "1", deleteAmount, value => deleteAmount = value, onTyping: value => deleteAmount = value);
            builder.Last.SetFixedWidth(120);
            builder.AddButton(Text.Get("delete_quantity_2"), () => ConfirmDelete(false));
            builder.Last.SetFixedWidth(210);
            builder.AddButton(Text.Get("delete_stack"), () => ConfirmDelete(true));
            builder.Last.SetFixedWidth(180);
            builder.End();
        }

        void CopyExistingItem(bool entireStack)
        {
            try
            {
                GameItem item = editor.Target;
                int amount = entireStack ? item.unitCount : ParseInt(copyAmount);
                var result = ExistingItems.Copy(item, amount, copyDestination);
                if (result.SpawnedCount > 0)
                {
                    editor.Item.unitCount = item.unitCount;
                    previewNeedsRefresh = true;
                    RequestRebuild();
                }
                SetStatus(result.Message);
            }
            catch (Exception error)
            {
                NicoItemManagerMod.Log?.Warning("Copy existing item: " + error);
                SetStatus(error.Message);
            }
        }

        void ApplyExistingItem()
        {
            try
            {
                GameItem item = editor.Target;
                editor.ApplyExisting();
                OpenExistingItem(item);
                SetStatus(Text.Get("changes_applied_to_the_existing_item_saved"));
            }
            catch (Exception error)
            {
                NicoItemManagerMod.Log?.Warning("Apply existing item: " + error);
                SetStatus(error.Message);
            }
        }

        void ConfirmDelete(bool entireStack)
        {
            try
            {
                GameItem item = ExistingItems.RequireEditable(editor.Target);
                int amount = entireStack ? item.unitCount : ParseInt(deleteAmount);
                if (amount < 1 || amount > item.unitCount)
                    throw new InvalidOperationException(Text.Get("choose_1_items_to_delete", item.unitCount));
                if (item.FindAllChildItems().Count > 0)
                    throw new InvalidOperationException(Text.Get("empty_the_container_before_deleting_it"));
                pendingDelete = amount;
                editorLayoutDirty = true;
                RequestRebuild();
            }
            catch (Exception error) { SetStatus(error.Message); }
        }

        void DeleteExistingItem()
        {
            if (!pendingDelete.HasValue) return;
            try
            {
                GameItem item = editor.Target;
                int amount = pendingDelete.Value;
                string name = item.GetDisplayName();
                ExistingItems.Delete(item, amount);
                pendingDelete = null;
                if (ItemSpawner.IsAlive(item)) OpenExistingItem(item);
                else BackToCatalog();
                SetStatus(Text.Get("deleted", name, amount));
            }
            catch (Exception error)
            {
                NicoItemManagerMod.Log?.Warning("Delete existing item: " + error);
                pendingDelete = null;
                editorLayoutDirty = true;
                RequestRebuild();
                SetStatus(error.Message);
            }
        }
    }
}
