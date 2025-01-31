﻿using Timespinner.GameAbstractions.Gameplay;
using Timespinner.GameObjects.BaseClasses;
using TsRandomizer.IntermediateObjects;
using TsRandomizer.Screens;

namespace TsRandomizer.LevelObjects.Other
{
	[TimeSpinnerType("Timespinner.GameObjects.Events.EnvironmentPrefabs.L11_Lab.EnvPrefabLabVilete")]
	// ReSharper disable once UnusedMember.Global
	class GlowingFloorEvent : LevelObject
	{
		bool teleportEnabled = false;
		bool teleportTriggered = false;
		public GlowingFloorEvent(Mobile typedObject) : base(typedObject)
		{
			if (Dynamic.Level.ID != 16)
				return;
			Dynamic._textPromptOffsetX = -20;
			Dynamic._textPromptText = "Exit the pyramid";
		}

		protected override void OnUpdate(GameplayScreen gameplayScreen)
		{
			if (Dynamic.Level.ID != 16)
				return;
			if (teleportTriggered)
				return;
			if (Scripts.Count != 0)
			{
				teleportEnabled = true;
				Scripts.Clear();
			}
			if (teleportEnabled && Scripts.Count == 0)
			{
				LevelReflected.JukeBox.StopSong();
				LevelReflected.RequestChangeLevel(new LevelChangeRequest
				{
					LevelID = 15,
					RoomID = 0,
					IsUsingWarp = true,
					IsUsingWhiteFadeOut = true,
					FadeInTime = 0.5f,
					FadeOutTime = 0.25f
				}); // Return to Pyramid start
				LevelReflected.JukeBox.PlaySong(Timespinner.GameAbstractions.EBGM.Level15);
				teleportTriggered = true;
			}
		}
	}
}
