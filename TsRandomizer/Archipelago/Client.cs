﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using Microsoft.Xna.Framework;
using TsRandomizer.Randomisation;
using TsRandomizer.Screens;

namespace TsRandomizer.Archipelago
{
	static class Client
	{
		public const int ConnectionTimeoutInSeconds = 10;

		static ArchipelagoSession session;

		static volatile bool hasConnectionResult;
		static ArchipelagoPacketBase connectionResult;

		public static volatile bool HasItemLocationInfo;
		public static LocationInfoPacket LocationScoutResult;

		static DataCache chache = new DataCache();

		public static volatile int Slot = -1;

		static ConcurrentQueue<ReceivedItem> receivedItemsQueue = new ConcurrentQueue<ReceivedItem>();

		static int receivedItemIndex;

		public static bool IsConnected;

		public static ItemLocationMap ItemLocations;

		static string serverUrl;
		static string userName;
		static string password;

		public static ConnectionResult CachedConnectionResult;

		public static ConnectionResult Connect(string server, string user, string pass)
		{
			if (IsConnected)
			{
				if (serverUrl == server && userName == user && password == pass)
					return CachedConnectionResult;
				
				Disconnect();
			}

			serverUrl = server;
			userName = user;
			password = pass;
			
			session = new ArchipelagoSession(serverUrl);
			session.PacketReceived += PackacedReceived;

			session.ConnectAsync();

			chache.LoadCache();

			hasConnectionResult = false;
			connectionResult = null;

			var connectedStartedTime = DateTime.UtcNow;

			while (!hasConnectionResult)
			{
				if (DateTime.UtcNow - connectedStartedTime > TimeSpan.FromSeconds(ConnectionTimeoutInSeconds))
				{
					Disconnect();

					CachedConnectionResult = new ConnectionFailed("Connection Timedout");
					return CachedConnectionResult;
				}

				Thread.Sleep(100);
			}

			if (connectionResult is ConnectionRefusedPacket refused)
			{
				Disconnect();

				CachedConnectionResult = new ConnectionFailed(string.Join(", ", refused.Errors));
				return CachedConnectionResult;
			}
			if (connectionResult is ConnectedPacket success)
			{
				IsConnected = true;

				CachedConnectionResult = new Connected(success);
				return CachedConnectionResult;
			}

			Disconnect();

			CachedConnectionResult = new ConnectionFailed("Unknown package, probably due to version missmatch");
			return CachedConnectionResult;
		}

		public static void Disconnect()
		{
			session?.DisconnectAsync();

			receivedItemIndex = 0;
			Slot = -1;

			IsConnected = false;

			chache = new DataCache();
			receivedItemsQueue = new ConcurrentQueue<ReceivedItem>();

			hasConnectionResult = false;
			HasItemLocationInfo = false;

			ItemLocations = null;
			session = null;

			CachedConnectionResult = null;
		}

		public static IEnumerable<ReceivedItem> GetReceivedItems()
		{
			while(receivedItemsQueue.TryDequeue(out var itemIdentifier))
				yield return itemIdentifier;
		}

		public static void SetStatus(ArchipelagoClientState status)
		{
			SendPacket(new StatusUpdatePacket { Status = status });
		}

		static void PackacedReceived(ArchipelagoPacketBase packet)
		{
			switch (packet)
			{
				case RoomInfoPacket roomInfoPacket: OnRoomInfoPacketReceived(roomInfoPacket); break;
				case DataPackagePacket dataPacket: OnDataPackagePacketReceived(dataPacket); break;
				case ConnectionRefusedPacket connectionRefusedPacket: OnConnectionRefusedPacketReceived(connectionRefusedPacket); break;
				case ConnectedPacket connectedPacket: OnConnectedPacketReceived(connectedPacket); break;
				case LocationInfoPacket locationInfoPacket: OnLocationInfoPacketReceived(locationInfoPacket); break;
				case ReceivedItemsPacket receivedItemsPacket: OnReceivedItemsPacketReceived(receivedItemsPacket); break;
				case PrintPacket printPacket: OnPrintPacketReceived(printPacket); break;
				case PrintJsonPacket printJsonPacket: OnPrinJsontPacketReceived(printJsonPacket); break;
			}
		}

		public static void SendPacket(ArchipelagoPacketBase packet)
		{
			session?.SendPacket(packet);
		}

		static void OnRoomInfoPacketReceived(RoomInfoPacket packet)
		{
			chache.UpdatePlayerNames(packet.Players);
		
			if (packet is RoomUpdatePacket)
				return;

			chache.Verify(packet.DataPackageVersions);

			var connectionRequest = new ConnectPacket
			{
				Game = "Timespinner",
				Name = userName,
				Password = password,
				Version = new Version(0, 1, 8),
				Uuid = "297802A3-63F5-433C-A200-11D03C870B56", //TODO Fixme, should be unique per save
				Tags = new List<string>(0)
			};

			session.SendPacket(connectionRequest);
		}

		static void OnConnectedPacketReceived(ConnectedPacket connectedPacket)
		{
			Slot = connectedPacket.Slot;

			chache.UpdatePlayerNames(connectedPacket.Players);

			hasConnectionResult = true;
			connectionResult = connectedPacket;
		}

		static void OnConnectionRefusedPacketReceived(ConnectionRefusedPacket connectionRefusedPacket)
		{
			hasConnectionResult = true;
			connectionResult = connectionRefusedPacket;
		}

		public static void RequestGameData(List<string> gamesToExcludeFromUpdate)
		{
			var getGameDataPacket = new GetDataPackagePacket
			{
				Exclusions = gamesToExcludeFromUpdate
			};

			session.SendPacket(getGameDataPacket);
		}

		static void OnDataPackagePacketReceived(DataPackagePacket dataPacket)
		{
			chache.Update(dataPacket.DataPackage.Games);
		}

		static void OnLocationInfoPacketReceived(LocationInfoPacket locationInfoPacket)
		{
			HasItemLocationInfo = true;
			LocationScoutResult = locationInfoPacket;
		}

		static void OnReceivedItemsPacketReceived(ReceivedItemsPacket receivedItemsPacket)
		{
			if (receivedItemsPacket.Index != receivedItemIndex)
			{
				receivedItemIndex = 0;
				ReSync();
			}
			else
			{
				receivedItemIndex += receivedItemsPacket.Items.Count;
			}

			foreach (var item in receivedItemsPacket.Items)
				receivedItemsQueue.Enqueue(
					new ReceivedItem
					{
						PlayerFrom = item.Player,
						ItemIdentifier = ItemMap.GetItemIdentifier(item.Item)
					});
		}

		static void ReSync()
		{
			Interlocked.Exchange(ref receivedItemsQueue, new ConcurrentQueue<ReceivedItem>());

			session.SendMultiplePackets(new SyncPacket(), GetLocationChecksPacket());
		}

		static void OnPrintPacketReceived(PrintPacket printPacket)
		{
			if (printPacket.Text == null)
				return;

			var lines = printPacket.Text.Split('\n');

			foreach (var line in lines)
				ScreenManager.Log.Add(line);
		}

		static void OnPrinJsontPacketReceived(PrintJsonPacket printJsonPacket)
		{
			var parts = new List<Part>();

			foreach (var messagePart in printJsonPacket.Data)
				parts.Add(new Part(GetMessage(messagePart), GetColor(messagePart)));

			ScreenManager.Log.Add(parts.ToArray());
		}

		static string GetMessage(JsonMessagePart messagePart)
		{
			switch (messagePart.Type)
			{
				case JsonMessagePartType.PlayerId:
					return chache.GetPlayerName(int.Parse(messagePart.Text));
				case JsonMessagePartType.ItemId:
					return chache.GetItemName(int.Parse(messagePart.Text));
				case JsonMessagePartType.LocationId:
					return chache.GetLocationName(int.Parse(messagePart.Text));
				default:
					return messagePart.Text;
			}
		}

		static Color GetColor(JsonMessagePart messagePart)
		{
			switch (messagePart.Color)
			{
				case JsonMessagePartColor.Red:
					return Color.Red;
				case JsonMessagePartColor.Green:
					return Color.Green;
				case JsonMessagePartColor.Yellow:
					return Color.Yellow;
				case JsonMessagePartColor.Blue:
					return Color.Blue;
				case JsonMessagePartColor.Magenta:
					return Color.Magenta;
				case JsonMessagePartColor.Cyan:
					return Color.Cyan;
				case JsonMessagePartColor.Black:
					return Color.DarkGray;
				case JsonMessagePartColor.White:
					return Color.White;
				case null:
					return GetColorFromPartType(messagePart.Type);
				default:
					return Color.White;
			}
		}

		static Color GetColorFromPartType(JsonMessagePartType? messagePartType)
		{
			switch (messagePartType)
			{
				case JsonMessagePartType.PlayerId:
					return Color.Orange;
				case JsonMessagePartType.ItemId:
					return Color.Crimson;
				case JsonMessagePartType.LocationId:
					return Color.Aquamarine;
				default:
					return Color.White;
			}
		}

		public static void UpdateChecks(ItemLocationMap itemLocationMap)
		{
			ItemLocations = itemLocationMap;

			session.SendPacket(GetLocationChecksPacket());
		}

		static LocationChecksPacket GetLocationChecksPacket()
		{
			if(ItemLocations == null)
				return new LocationChecksPacket { Locations = new List<int>(0) };

			return new LocationChecksPacket
			{
				Locations = ItemLocations
					.Where(l => l.IsPickedUp && !(l is ExteralItemLocation))
					.Select(l => LocationMap.GetLocationId(l.Key))
					.ToList()
			};
		}
	}
}