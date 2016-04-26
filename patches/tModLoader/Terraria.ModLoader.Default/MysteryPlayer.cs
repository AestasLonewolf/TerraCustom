using System;
using System.Collections.Generic;
using System.IO;
using Terraria.ModLoader.Exceptions;

namespace Terraria.ModLoader.Default
{
	public class MysteryPlayer : ModPlayer
	{
		private IList<UnloadedData> data;

		internal void AddData(string mod, string name, byte[] newData)
		{
			data.Add(new UnloadedData(mod, name, newData));
		}

		internal void RestoreData(Player player)
		{
			int k = 0;
			while (k < data.Count)
			{
				Mod mod = ModLoader.GetMod(data[k].modName);
				ModPlayer modPlayer = mod == null ? null : player.GetModPlayer(mod, data[k].name);
				if (modPlayer == null)
				{
					k++;
				}
				else
				{
					using (MemoryStream memoryStream = new MemoryStream(data[k].data))
					{
						using (BinaryReader reader = new BinaryReader(memoryStream))
						{
                            try
                            {
                                modPlayer.LoadCustomData(reader);
                            }
                            catch (Exception e)
                            {
                                throw new CustomModDataException(mod,
                                    "Error in loading custom player data for " + mod.Name, e);
                            }
						}
					}
					data.RemoveAt(k);
				}
			}
		}

		public override void Initialize()
		{
			data = new List<UnloadedData>();
		}

		public override void SaveCustomData(BinaryWriter writer)
		{
			if (data.Count > 0)
			{
				writer.Write((ushort)data.Count);
				foreach (UnloadedData unloadedData in data)
				{
					writer.Write(unloadedData.modName);
					writer.Write(unloadedData.name);
					writer.Write((ushort)unloadedData.data.Length);
					writer.Write(unloadedData.data);
				}
			}
		}

		public override void LoadCustomData(BinaryReader reader)
		{
			int count = reader.ReadUInt16();
			for (int k = 0; k < count; k++)
			{
				string modName = reader.ReadString();
				string name = reader.ReadString();
				byte[] unloadedData = reader.ReadBytes(reader.ReadUInt16());
				AddData(modName, name, unloadedData);
			}
		}

		public override void SetupStartInventory(IList<Item> items)
		{
			if (AprilFools.CheckAprilFools())
			{
				Item item = new Item();
				item.SetDefaults(mod.ItemType("AprilFools"));
				items.Add(item);
			}
		}

		private struct UnloadedData
		{
			internal string modName;
			internal string name;
			internal byte[] data;

			internal UnloadedData(string mod, string name, byte[] data)
			{
				this.modName = mod;
				this.name = name;
				this.data = data;
			}
		}
	}
}
