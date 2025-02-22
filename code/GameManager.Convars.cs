using Sandbox;

namespace TTT;

public partial class GameManager
{
#if DEBUG
	#region Debug
	[ConVar.Server( "ttt_round_debug", Help = "Stop the in progress round from ending.", Saved = true )]
	public static bool PreventWin { get; set; }
	#endregion
#endif

	#region Round
	[ConVar.Server( "ttt_preround_time", Help = "The length of the preround time.", Saved = true )]
	public static int PreRoundTime { get; set; } = 20;

	[ConVar.Server( "ttt_inprogress_time", Help = "The length of the in progress round time.", Saved = true )]
	public static int InProgressTime { get; set; } = 300;

	[ConVar.Server( "ttt_inprogress_secs_per_death", Help = "The number of seconds to add to the in progress round timer when someone dies.", Saved = true )]
	public static int InProgressSecondsPerDeath { get; set; } = 15;

	[ConVar.Server( "ttt_postround_time", Help = "The length of the postround time.", Saved = true )]
	public static int PostRoundTime { get; set; } = 15;

	[ConVar.Server( "ttt_mapselection_time", Help = "The length of the map selection period.", Saved = true )]
	public static int MapSelectionTime { get; set; } = 15;
	#endregion

	#region Map
	[ConVar.Server( "ttt_default_map", Help = "The default map to swap to if no maps are found.", Saved = true )]
	public static string DefaultMap { get; set; } = "facepunch.flatgrass";

	[ConVar.Server( "ttt_rtv_threshold", Help = "The percentage of players needed to RTV.", Saved = true )]
	public static float RTVThreshold { get; set; } = 0.66f;

	[ConVar.Replicated( "ttt_round_limit", Help = "The maximum amount of rounds that can be played before a map vote is forced.", Saved = true )]
	public static int RoundLimit { get; set; } = 6;

	[ConVar.Replicated( "ttt_time_limit", Saved = true, Help = "The number of seconds before a map vote is forced." ), Change( nameof( UpdateTimeLimit ) )]
	private static int TimeLimit { get; set; } = 4500;

	public static void UpdateTimeLimit( int _, int newValue )
	{
		Current.TimeUntilMapSwitch = newValue;
	}
	#endregion

	#region Minimum Players
	[ConVar.Replicated( "ttt_min_players", Help = "The minimum players to start the game.", Saved = true )]
	public static int MinPlayers { get; set; } = 2;
	#endregion

	#region AFK Timers
	[ConVar.Replicated( "ttt_afk_timer", Help = "The amount of time before a player is forced to being a spectator.", Saved = true )]
	public static int AFKTimer { get; set; } = 180;
	#endregion

	#region Credits
	[ConVar.Server( "ttt_credits_award_pct", Help = "When this percentage of Innocents are dead, Traitors are given credits.", Saved = true )]
	public static float CreditsAwardPercentage { get; set; } = 0.35f;

	[ConVar.Server( "ttt_credits_award_size", Help = "The number of credits awarded when the percentage is reached.", Saved = true )]
	public static int CreditsAwarded { get; set; } = 100;

	[ConVar.Server( "ttt_credits_traitordeath", Help = "The number of credits Detectives receive when a Traitor dies.", Saved = true )]
	public static int DetectiveTraitorDeathReward { get; set; } = 100;

	[ConVar.Server( "ttt_credits_detectivekill", Help = "The number of credits a Traitor receives when they kill a Detective.", Saved = true )]
	public static int TraitorDetectiveKillReward { get; set; } = 100;
	#endregion

	#region Voice Chat
	[ConVar.Replicated( "ttt_proximity_chat", Saved = true ), Change( nameof( UpdateVoiceChat ) )]
	public static bool ProximityChat { get; set; }

	public static void UpdateVoiceChat( bool _, bool newValue )
	{
		foreach ( var client in Game.Clients )
		{
			if ( client.Pawn is not Player player || !player.IsAlive )
				continue;

			client.Voice.WantsStereo = newValue;
		}
	}
	#endregion

	[ConVar.Server( "ttt_avatar_clothing" )]
	public static bool AvatarClothing { get; set; }
}
