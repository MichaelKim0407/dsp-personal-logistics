﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PersonalLogistics.Util;

namespace PersonalLogistics.Shipping
{
    [Serializable]
    public class Cost
    {
        public long energyCost;
        public int planetId;
        public int stationId;
        public bool needWarper;
        public bool paid;
    }

    [Serializable]
    public class InventoryItem
    {
        public int itemId;
        public string itemName;
        public int count;
        private long _lastUpdated;

        public long AgeInSeconds => (GameMain.gameTick - _lastUpdated) / 60;

        public long LastUpdated
        {
            get => _lastUpdated;
            set => _lastUpdated = value;
        }

        public static InventoryItem Import(BinaryReader r)
        {
            var result = new InventoryItem
            {
                itemId = r.ReadInt32(),
                count = r.ReadInt32(),
                _lastUpdated = r.ReadInt64()
            };

            result.itemName = ItemUtil.GetItemName(result.itemId);

            return result;
        }

        public void Export(BinaryWriter binaryWriter)
        {
            binaryWriter.Write(itemId);
            binaryWriter.Write(count);
            binaryWriter.Write(_lastUpdated);
        }
    }

    public class ItemBuffer
    {
        public int version = 2;
        public int seed;
        public List<InventoryItem> inventoryItems = new List<InventoryItem>();
        public Dictionary<int, InventoryItem> inventoryItemLookup = new Dictionary<int, InventoryItem>();

        public void Remove(InventoryItem inventoryItem)
        {
            if (!inventoryItems.Remove(inventoryItem))
            {
                Log.Warn($"Failed to actually remove inventoryItem {inventoryItem} from invItems");
            }

            if (!inventoryItemLookup.Remove(inventoryItem.itemId))
            {
                Log.Warn($"Lookup key not found for item id {inventoryItem.itemId}");
            }
        }

        public static ItemBuffer Import(BinaryReader r)
        {
            var result = new ItemBuffer
            {
                version = r.ReadInt32(),
                seed = r.ReadInt32()
            };
            int length = r.ReadInt32();
            Log.Debug($"Import length = {length}");

            var itemsToDelete = new List<InventoryItem>();

            for (var i = 0; i < length; i++)
            {
                var inventoryItem = InventoryItem.Import(r);
                if (result.inventoryItemLookup.ContainsKey(inventoryItem.itemId))
                {
                    Log.Warn($"Multiple inv items for {inventoryItem.itemName} found, combining");
                    result.inventoryItemLookup[inventoryItem.itemId].count += inventoryItem.count;
                    itemsToDelete.Add(inventoryItem);
                }
                else
                {
                    result.inventoryItems.Add(inventoryItem);
                    result.inventoryItemLookup[inventoryItem.itemId] = inventoryItem;
                }

                if (result.version == 1)
                {
                    // migrate lastUpdated
                    inventoryItem.LastUpdated = GameMain.gameTick;
                }
            }

            if (result.version < 2)
            {
                result.version = 2;
                Log.Debug($"migrated version {result.version} save to version 2");
            }

            foreach (var itemToDelete in itemsToDelete)
            {
                result.inventoryItems.Remove(itemToDelete);
            }

            return result;
        }

        public void Export(BinaryWriter w)
        {
            w.Write(version);
            w.Write(seed);
            w.Write(inventoryItems.Count);

            foreach (var inventoryItem in inventoryItems)
            {
                inventoryItem.Export(w);
            }
        }

        public override string ToString()
        {
            return $"version={version}, seed={seed}, invItems={inventoryItems.Count}";
        }
    }

    public static class ShippingStatePersistence
    {
        public static ItemBuffer LoadState(int seed)
        {
            Log.Debug($"load state for seed {seed}");
            var path = GetPath(seed);
            if (!File.Exists(path))
            {
                Log.Info($"PersonalLogistics.{seed}.save not found, path: {path}");
                var state = new ItemBuffer
                {
                    seed = seed,
                    version = 1,
                    inventoryItems = new List<InventoryItem>(),
                    inventoryItemLookup = new Dictionary<int, InventoryItem>()
                };
                SaveState(state);
                return state;
            }

            try
            {
                using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    using (BinaryReader r = new BinaryReader(fileStream))
                    {
                        return ItemBuffer.Import(r);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn($"Failed to load saved shipping data {e.Message}  {e.StackTrace}");
            }

            return new ItemBuffer
            {
                seed = seed
            };
        }


        public static string saveFolder
        {
            get
            {
                if (savePath == null)
                {
                    savePath = new StringBuilder(GameConfig.overrideDocumentFolder).Append(GameConfig.gameName).Append("/PersonalLogistics/").ToString();
                    if (!Directory.Exists(savePath))
                        Directory.CreateDirectory(savePath);
                }

                return savePath;
            }
        }

        private static string savePath = null;


        private static string GetPath(int seed)
        {
            return Path.Combine(saveFolder, $"PersonalLogistics.{seed}.save");
        }

        public static void SaveState(ItemBuffer itemBuffer)
        {
            try
            {
                using (FileStream fileStream = new FileStream(GetPath(itemBuffer.seed), FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (BinaryWriter w = new BinaryWriter(fileStream))
                    {
                        itemBuffer.Export(w);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to save state for seed {itemBuffer.seed} {ex.Message} {ex.StackTrace}");
            }
        }
    }
}