using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NicoItemManager
{
    internal sealed class ItemPreset
    {
        public string Id, Name, ItemId;
        public ItemConfiguration Configuration;
    }

    internal sealed class ItemPresets
    {
        const string FileName = "item-presets.json";
        readonly List<ItemPreset> entries = new List<ItemPreset>();
        internal IReadOnlyList<ItemPreset> Entries => entries;
        bool loaded;
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new PresetTagConverter() },
            Formatting = Formatting.Indented
        };

        sealed class Document
        {
            [JsonProperty(Required = Required.Always)] public int Version = 1;
            [JsonProperty(Required = Required.Always)] public List<ItemPreset> Items = new List<ItemPreset>();
        }

        internal void Load()
        {
            if (loaded) return;
            string json = ModHelper.ReadModDataFileText(NicoItemManagerMod.Id, FileName);
            var document = json == null ? new Document() : JsonConvert.DeserializeObject<Document>(json, Settings);
            if (document == null || document.Version != 1 || document.Items == null
                || document.Items.Any(item => item == null || string.IsNullOrWhiteSpace(item.Id)
                    || string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.ItemId)
                    || item.Configuration == null)
                || document.Items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != document.Items.Count)
                throw new InvalidOperationException(Text.Get("invalid_custom_item_file_the_original_file"));
            entries.AddRange(document.Items);
            loaded = true;
        }

        internal ItemPreset Save(string name, string itemId, ItemConfiguration configuration, string replaceId = null)
        {
            Load();
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 80)
                throw new InvalidOperationException(Text.Get("use_1_80_characters_for_the_preset"));
            var saved = new ItemPreset { Id = replaceId ?? Guid.NewGuid().ToString("N"), Name = name,
                ItemId = itemId, Configuration = configuration };
            var next = new List<ItemPreset>(entries);
            int index = replaceId == null ? -1 : next.FindIndex(item => item.Id == replaceId);
            if (replaceId != null && index < 0) throw new InvalidOperationException(Text.Get("the_preset_no_longer_exists"));
            if (index < 0) next.Add(saved);
            else next[index] = saved;
            Persist(next);
            return saved;
        }

        internal void Delete(string id)
        {
            Load();
            Persist(entries.Where(item => item.Id != id).ToList());
        }

        void Persist(List<ItemPreset> next)
        {
            ModHelper.WriteModDataFileText(NicoItemManagerMod.Id, FileName,
                JsonConvert.SerializeObject(new Document { Items = next }, Settings));
            entries.Clear();
            entries.AddRange(next);
        }

        // Native TagState has two constructors; explicitly serialize just its value record.
        sealed class PresetTagConverter : JsonConverter<TagState>
        {
            public override void WriteJson(JsonWriter writer, TagState value, JsonSerializer serializer)
            {
                if (value == null) { writer.WriteNull(); return; }
                serializer.Serialize(writer, new { value.identifier, value.identifierName, value.valueEnabled,
                    value.valueString, value.valueInt, value.valueFloat, value.valueLong, value.valueDouble, value.valueBool });
            }

            public override TagState ReadJson(JsonReader reader, Type objectType, TagState existingValue,
                bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return null;
                JObject tag = JObject.Load(reader);
                return new TagState((string)tag["identifier"], (string)tag["identifierName"])
                {
                    valueEnabled = (bool)tag["valueEnabled"], valueString = (string)tag["valueString"],
                    valueInt = (int)tag["valueInt"], valueFloat = (float)tag["valueFloat"],
                    valueLong = (long)tag["valueLong"], valueDouble = (double)tag["valueDouble"],
                    valueBool = (bool)tag["valueBool"]
                };
            }
        }
    }
}
