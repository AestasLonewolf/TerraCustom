using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using Terraria.ModLoader.Default;

namespace Terraria.ModLoader.IO
{
	internal static class TileIO
	{
		//in Terraria.IO.WorldFile.SaveWorldTiles add type check to tile.active() check and wall check
		internal struct TileTables
		{
			internal IDictionary<ushort, ushort> tiles;
			internal IDictionary<ushort, bool> frameImportant;
			internal IDictionary<ushort, ushort> walls;
			internal IDictionary<ushort, string> tileModNames;
			internal IDictionary<ushort, string> tileNames;

			internal static TileTables Create()
			{
				TileTables tables = new TileTables();
				tables.tiles = new Dictionary<ushort, ushort>();
				tables.frameImportant = new Dictionary<ushort, bool>();
				tables.walls = new Dictionary<ushort, ushort>();
				tables.tileModNames = new Dictionary<ushort, string>();
				tables.tileNames = new Dictionary<ushort, string>();
				return tables;
			}
		}

		internal static bool WriteTiles(BinaryWriter writer)
		{
			ISet<ushort> types = new HashSet<ushort>();
			ISet<ushort> walls = new HashSet<ushort>();
			for (int i = 0; i < Main.maxTilesX; i++)
			{
				for (int j = 0; j < Main.maxTilesY; j++)
				{
					Tile tile = Main.tile[i, j];
					if (tile.active() && tile.type >= TileID.Count)
					{
						types.Add(tile.type);
					}
					if (tile.wall >= WallID.Count)
					{
						walls.Add(tile.wall);
					}
				}
			}
			if (types.Count > 0 || walls.Count > 0)
			{
				writer.Write((ushort)types.Count);
				foreach (ushort type in types)
				{
					writer.Write(type);
					ModTile modTile = TileLoader.GetTile(type);
					writer.Write(modTile.mod.Name);
					writer.Write(modTile.Name);
					writer.Write(Main.tileFrameImportant[type]);
				}
				writer.Write((ushort)walls.Count);
				foreach (ushort wall in walls)
				{
					writer.Write(wall);
					ModWall modWall = WallLoader.GetWall(wall);
					writer.Write(modWall.mod.Name);
					writer.Write(modWall.Name);
				}
				WriteTileData(writer);
				return true;
			}
			return false;
		}

		internal static void ReadTiles(BinaryReader reader)
		{
			TileTables tables = TileTables.Create();
			ushort count = reader.ReadUInt16();
			for (int k = 0; k < count; k++)
			{
				ushort type = reader.ReadUInt16();
				string modName = reader.ReadString();
				string name = reader.ReadString();
				Mod mod = ModLoader.GetMod(modName);
				tables.tiles[type] = mod == null ? (ushort)0 : (ushort)mod.TileType(name);
				if (tables.tiles[type] == 0)
				{
					tables.tiles[type] = (ushort)ModLoader.GetMod("ModLoader").TileType("PendingMysteryTile");
					tables.tileModNames[type] = modName;
					tables.tileNames[type] = name;
				}
				tables.frameImportant[type] = reader.ReadBoolean();
			}
			count = reader.ReadUInt16();
			for (int k = 0; k < count; k++)
			{
				ushort wall = reader.ReadUInt16();
				string modName = reader.ReadString();
				string name = reader.ReadString();
				Mod mod = ModLoader.GetMod(modName);
				tables.walls[wall] = mod == null ? (ushort)0 : (ushort)mod.WallType(name);
			}
			ReadTileData(reader, tables);
		}

		internal static void WriteTileData(BinaryWriter writer)
		{
			byte skip = 0;
			bool nextModTile = false;
			int i = 0;
			int j = 0;
			do
			{
				Tile tile = Main.tile[i, j];
				if (HasModData(tile))
				{
					if (!nextModTile)
					{
						writer.Write(skip);
						skip = 0;
					}
					else
					{
						nextModTile = false;
					}
					WriteModTile(ref i, ref j, writer, ref nextModTile);
				}
				else
				{
					skip++;
					if (skip == 255)
					{
						writer.Write(skip);
						skip = 0;
					}
				}
			}
			while (NextTile(ref i, ref j));
			if (skip > 0)
			{
				writer.Write(skip);
			}
		}

		internal static void ReadTileData(BinaryReader reader, TileTables tables)
		{
			int i = 0;
			int j = 0;
			bool nextModTile = false;
			do
			{
				if (!nextModTile)
				{
					byte skip = reader.ReadByte();
					while (skip == 255)
					{
						for (byte k = 0; k < 255; k++)
						{
							if (!NextTile(ref i, ref j))
							{
								return;
							}
						}
						skip = reader.ReadByte();
					}
					for (byte k = 0; k < skip; k++)
					{
						if (!NextTile(ref i, ref j))
						{
							return;
						}
					}
				}
				else
				{
					nextModTile = false;
				}
				ReadModTile(ref i, ref j, tables, reader, ref nextModTile);
			}
			while (NextTile(ref i, ref j));
		}

		internal static void WriteModTile(ref int i, ref int j, BinaryWriter writer, ref bool nextModTile)
		{
			Tile tile = Main.tile[i, j];
			byte flags = 0;
			byte[] data = new byte[11];
			int index = 1;
			if (tile.active() && tile.type >= TileID.Count)
			{
				flags |= 1;
				data[index] = (byte)tile.type;
				index++;
				data[index] = (byte)(tile.type >> 8);
				index++;
				if (Main.tileFrameImportant[tile.type])
				{
					data[index] = (byte)tile.frameX;
					index++;
					if (tile.frameX >= 256)
					{
						flags |= 2;
						data[index] = (byte)(tile.frameX >> 8);
						index++;
					}
					data[index] = (byte)tile.frameY;
					index++;
					if (tile.frameY >= 256)
					{
						flags |= 4;
						data[index] = (byte)(tile.frameY >> 8);
						index++;
					}
				}
				if (tile.color() != 0)
				{
					flags |= 8;
					data[index] = tile.color();
					index++;
				}
			}
			if (tile.wall >= WallID.Count)
			{
				flags |= 16;
				data[index] = (byte)tile.wall;
				index++;
				data[index] = (byte)(tile.wall >> 8);
				index++;
				if (tile.wallColor() != 0)
				{
					flags |= 32;
					data[index] = tile.wallColor();
					index++;
				}
			}
			int nextI = i;
			int nextJ = j;
			byte sameCount = 0;
			while (NextTile(ref nextI, ref nextJ))
			{
				if (tile.isTheSameAs(Main.tile[nextI, nextJ]) && sameCount < 255)
				{
					sameCount++;
					i = nextI;
					j = nextJ;
				}
				else if (HasModData(Main.tile[nextI, nextJ]))
				{
					flags |= 128;
					nextModTile = true;
					break;
				}
				else
				{
					break;
				}
			}
			if (sameCount > 0)
			{
				flags |= 64;
				data[index] = sameCount;
				index++;
			}
			data[0] = flags;
			writer.Write(data, 0, index);
		}

		internal static void ReadModTile(ref int i, ref int j, TileTables tables, BinaryReader reader, ref bool nextModTile)
		{
			byte flags;
			flags = reader.ReadByte();
			Tile tile = Main.tile[i, j];
			if ((flags & 1) == 1)
			{
				tile.active(true);
				ushort saveType = reader.ReadUInt16();
				tile.type = tables.tiles[saveType];
				if (tables.frameImportant[saveType])
				{
					if ((flags & 2) == 2)
					{
						tile.frameX = reader.ReadInt16();
					}
					else
					{
						tile.frameX = reader.ReadByte();
					}
					if ((flags & 4) == 4)
					{
						tile.frameY = reader.ReadInt16();
					}
					else
					{
						tile.frameY = reader.ReadByte();
					}
				}
				else
				{
					tile.frameX = -1;
					tile.frameY = -1;
				}
				if (tile.type == ModLoader.GetMod("ModLoader").TileType("PendingMysteryTile")
					&& tables.tileNames.ContainsKey(saveType))
				{
					MysteryTileInfo info;
					if (tables.frameImportant[saveType])
					{
						info = new MysteryTileInfo(tables.tileModNames[saveType], tables.tileNames[saveType],
							tile.frameX, tile.frameY);
					}
					else
					{
						info = new MysteryTileInfo(tables.tileModNames[saveType], tables.tileNames[saveType]);
					}
					MysteryTilesWorld modWorld = (MysteryTilesWorld)ModLoader.GetMod("ModLoader").GetModWorld("MysteryTilesWorld");
					int pendingFrameID = modWorld.pendingInfos.IndexOf(info);
					if (pendingFrameID < 0)
					{
						pendingFrameID = modWorld.pendingInfos.Count;
						modWorld.pendingInfos.Add(info);
					}
					MysteryTileFrame pendingFrame = new MysteryTileFrame(pendingFrameID);
					tile.frameX = pendingFrame.FrameX;
					tile.frameY = pendingFrame.FrameY;
				}
				if ((flags & 8) == 8)
				{
					tile.color(reader.ReadByte());
				}
				WorldGen.tileCounts[tile.type] += j <= Main.worldSurface ? 5 : 1;
			}
			if ((flags & 16) == 16)
			{
				tile.wall = tables.walls[reader.ReadUInt16()];
				if ((flags & 32) == 32)
				{
					tile.wallColor(reader.ReadByte());
				}
			}
			if ((flags & 64) == 64)
			{
				byte sameCount = reader.ReadByte();
				for (byte k = 0; k < sameCount; k++)
				{
					NextTile(ref i, ref j);
					Main.tile[i, j].CopyFrom(tile);
					WorldGen.tileCounts[tile.type] += j <= Main.worldSurface ? 5 : 1;
				}
			}
			if ((flags & 128) == 128)
			{
				nextModTile = true;
			}
		}

		private static bool HasModData(Tile tile)
		{
			return (tile.active() && tile.type >= TileID.Count) || tile.wall >= WallID.Count;
		}

		private static bool NextTile(ref int i, ref int j)
		{
			j++;
			if (j >= Main.maxTilesY)
			{
				j = 0;
				i++;
				if (i >= Main.maxTilesX)
				{
					return false;
				}
			}
			return true;
		}
		//in Terraria.IO.WorldFile.SaveWorldTiles for saving tile frames add
		//  short frameX = tile.frameX; TileIO.VanillaSaveFrames(tile, ref frameX);
		//  and replace references to tile.frameX with frameX
		internal static void VanillaSaveFrames(Tile tile, ref short frameX)
		{
			if (tile.type == TileID.Mannequin || tile.type == TileID.Womannequin)
			{
				int slot = tile.frameX / 100;
				int position = tile.frameY / 18;
				if (HasModArmor(slot, position))
				{
					frameX %= 100;
				}
			}
		}

		internal struct ContainerTables
		{
			internal IDictionary<int, int> headSlots;
			internal IDictionary<int, int> bodySlots;
			internal IDictionary<int, int> legSlots;

			internal static ContainerTables Create()
			{
				ContainerTables tables = new ContainerTables();
				tables.headSlots = new Dictionary<int, int>();
				tables.bodySlots = new Dictionary<int, int>();
				tables.legSlots = new Dictionary<int, int>();
				return tables;
			}
		}
		//in Terraria.GameContent.Tile_Entities.TEItemFrame.WriteExtraData
		//  if item is a mod item write 0 as the ID
		internal static bool WriteContainers(BinaryWriter writer)
		{
			byte[] flags = new byte[1];
			byte numFlags = 0;
			ISet<int> headSlots = new HashSet<int>();
			ISet<int> bodySlots = new HashSet<int>();
			ISet<int> legSlots = new HashSet<int>();
			IDictionary<int, int> itemFrames = new Dictionary<int, int>();
			for (int i = 0; i < Main.maxTilesX; i++)
			{
				for (int j = 0; j < Main.maxTilesY; j++)
				{
					Tile tile = Main.tile[i, j];
					if (tile.active() && (tile.type == TileID.Mannequin || tile.type == TileID.Womannequin))
					{
						int slot = tile.frameX / 100;
						int position = tile.frameY / 18;
						if (HasModArmor(slot, position))
						{
							if (position == 0)
							{
								headSlots.Add(slot);
							}
							else if (position == 1)
							{
								bodySlots.Add(slot);
							}
							else if (position == 2)
							{
								legSlots.Add(slot);
							}
							flags[0] |= 1;
							numFlags = 1;
						}
					}
				}
			}
			int tileEntity = 0;
			foreach (KeyValuePair<int, TileEntity> entity in TileEntity.ByID)
			{
				TEItemFrame itemFrame = entity.Value as TEItemFrame;
				if (itemFrame != null && ItemLoader.NeedsModSaving(itemFrame.item))
				{
					itemFrames.Add(itemFrame.ID, tileEntity);
					flags[0] |= 2;
					numFlags = 1;
				}
				tileEntity++;
			}
			if (numFlags == 0)
			{
				return false;
			}
			writer.Write(numFlags);
			writer.Write(flags, 0, numFlags);
			if ((flags[0] & 1) == 1)
			{
				writer.Write((ushort)headSlots.Count);
				foreach (int slot in headSlots)
				{
					writer.Write((ushort)slot);
					ModItem item = ItemLoader.GetItem(EquipLoader.slotToId[EquipType.Head][slot]);
					writer.Write(item.mod.Name);
					writer.Write(Main.itemName[item.item.type]);
				}
				writer.Write((ushort)bodySlots.Count);
				foreach (int slot in bodySlots)
				{
					writer.Write((ushort)slot);
					ModItem item = ItemLoader.GetItem(EquipLoader.slotToId[EquipType.Body][slot]);
					writer.Write(item.mod.Name);
					writer.Write(Main.itemName[item.item.type]);
				}
				writer.Write((ushort)legSlots.Count);
				foreach (int slot in legSlots)
				{
					writer.Write((ushort)slot);
					ModItem item = ItemLoader.GetItem(EquipLoader.slotToId[EquipType.Legs][slot]);
					writer.Write(item.mod.Name);
					writer.Write(Main.itemName[item.item.type]);
				}
				WriteContainerData(writer);
			}
			if ((flags[0] & 2) == 2)
			{
				writer.Write(itemFrames.Count);
				foreach (int oldID in itemFrames.Keys)
				{
					TEItemFrame itemFrame = TileEntity.ByID[oldID] as TEItemFrame;
					writer.Write(itemFrames[oldID]);
					ItemIO.WriteItem(itemFrame.item, writer, true);
				}
			}
			return true;
		}

		internal static void ReadContainers(BinaryReader reader)
		{
			byte[] flags = new byte[1];
			reader.Read(flags, 0, reader.ReadByte());
			if ((flags[0] & 1) == 1)
			{
				ContainerTables tables = ContainerTables.Create();
				int count = reader.ReadUInt16();
				for (int k = 0; k < count; k++)
				{
					int slot = reader.ReadUInt16();
					string modName = reader.ReadString();
					string name = reader.ReadString();
					Mod mod = ModLoader.GetMod(modName);
					tables.headSlots[slot] = mod == null ? 0 : mod.GetItem(name).item.headSlot;
				}
				count = reader.ReadUInt16();
				for (int k = 0; k < count; k++)
				{
					int slot = reader.ReadUInt16();
					string modName = reader.ReadString();
					string name = reader.ReadString();
					Mod mod = ModLoader.GetMod(modName);
					tables.bodySlots[slot] = mod == null ? 0 : mod.GetItem(name).item.bodySlot;
				}
				count = reader.ReadUInt16();
				for (int k = 0; k < count; k++)
				{
					int slot = reader.ReadUInt16();
					string modName = reader.ReadString();
					string name = reader.ReadString();
					Mod mod = ModLoader.GetMod(modName);
					tables.legSlots[slot] = mod == null ? 0 : mod.GetItem(name).item.legSlot;
				}
				ReadContainerData(reader, tables);
			}
			if ((flags[0] & 2) == 2)
			{
				int count = reader.ReadInt32();
				for (int k = 0; k < count; k++)
				{
					int id = reader.ReadInt32();
					TEItemFrame itemFrame = TileEntity.ByID[id] as TEItemFrame;
					ItemIO.ReadItem(itemFrame.item, reader, true);
				}
			}
		}

		internal static void WriteContainerData(BinaryWriter writer)
		{
			for (int i = 0; i < Main.maxTilesX; i++)
			{
				for (int j = 0; j < Main.maxTilesY; j++)
				{
					Tile tile = Main.tile[i, j];
					if (tile.active() && (tile.type == TileID.Mannequin || tile.type == TileID.Womannequin))
					{
						int slot = tile.frameX / 100;
						int frameX = tile.frameX % 100;
						int position = tile.frameY / 18;
						if (HasModArmor(slot, position) && frameX % 36 == 0)
						{
							writer.Write(i);
							writer.Write(j);
							writer.Write((byte)position);
							writer.Write((ushort)slot);
						}
					}
				}
			}
			writer.Write(-1);
		}

		internal static void ReadContainerData(BinaryReader reader, ContainerTables tables)
		{
			int i = reader.ReadInt32();
			while (i > 0)
			{
				int j = reader.ReadInt32();
				int position = reader.ReadByte();
				int slot = reader.ReadUInt16();
				Tile left = Main.tile[i, j];
				Tile right = Main.tile[i + 1, j];
				if (left.active() && right.active() && (left.type == TileID.Mannequin || left.type == TileID.Womannequin)
				    && left.type == right.type && (left.frameX == 0 || left.frameX == 36) && right.frameX == left.frameX + 18
				    && left.frameY / 18 == position && left.frameY == right.frameY)
				{
					if (position == 0)
					{
						slot = tables.headSlots[slot];
					}
					else if (position == 1)
					{
						slot = tables.bodySlots[slot];
					}
					else if (position == 2)
					{
						slot = tables.legSlots[slot];
					}
					left.frameX += (short)(100 * slot);
				}
				i = reader.ReadInt32();
			}
		}

		private static bool HasModArmor(int slot, int position)
		{
			if (position == 0)
			{
				return slot >= Main.numArmorHead;
			}
			else if (position == 1)
			{
				return slot >= Main.numArmorBody;
			}
			else if (position == 2)
			{
				return slot >= Main.numArmorLegs;
			}
			return false;
		}
	}
}
