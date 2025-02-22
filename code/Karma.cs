using Sandbox;
using System;
using System.Collections.Generic;

namespace TTT;

public static class Karma
{
	[ConVar.Replicated( "ttt_karma_enabled", Help = "Whether or not the karma system is enabled.", Saved = true )]
	public static bool Enabled { get; set; } = true;

	[ConVar.Server( "ttt_karma_low_autokick", Help = "Whether or not to kick a player with low karma.", Saved = true )]
	public static bool LowAutoKick { get; set; } = true;

	[ConVar.Server( "ttt_karma_start", Help = "The starting karma value a player begins with.", Saved = true )]
	public static int StartValue { get; set; } = 1000;

	[ConVar.Server( "ttt_karma_max", Help = "The maximum karma value a player can have.", Saved = true )]
	public static int MaxValue { get; set; } = 1100;

	[ConVar.Server( "ttt_karma_min", Help = "The minimum karma a player can have before they get kicked.", Saved = true )]
	public static int MinValue { get; set; } = 500;

	public static Dictionary<long, float> SavedPlayerValues { get; private set; } = new();

	public const float CleanBonus = 30;
	public const float FallOff = 0.25f;
	public const float RoundHeal = 5;

	public static float GetHurtReward( float damage, float multiplier )
	{
		return MaxValue * Math.Clamp( damage * multiplier, 0, 1 );
	}

	public static float GetHurtPenalty( float victimKarma, float damage, float multiplier )
	{
		return victimKarma * Math.Clamp( damage * multiplier, 0, 1 );
	}

	public static float GetKillReward( float multiplier )
	{
		return MaxValue * Math.Clamp( multiplier, 0, 1 );
	}

	public static float GetKillPenalty( float victimKarma, float multiplier )
	{
		return victimKarma * Math.Clamp( multiplier, 0, 1 );
	}

	private static void GivePenalty( Player player, float penalty )
	{
		player.ActiveKarma = Math.Max( player.ActiveKarma - penalty, 0 );
		player.TimeUntilClean = Math.Min( Math.Max( player.TimeUntilClean * penalty * 0.2f, penalty ), float.MaxValue );
	}

	private static void GiveReward( Player player, float reward )
	{
		reward = DecayMultiplier( player ) * reward;
		player.ActiveKarma = Math.Min( player.ActiveKarma + reward, MaxValue );
	}

	private static float DecayMultiplier( Player player )
	{
		if ( FallOff <= 0 || player.ActiveKarma < StartValue )
			return 1;

		if ( player.ActiveKarma >= MaxValue )
			return 1;

		var baseDiff = MaxValue - StartValue;
		var plyDiff = player.ActiveKarma - StartValue;
		var half = Math.Clamp( FallOff, 0.1f, 0.99f );

		return MathF.Exp( -0.69314718f / (baseDiff * half) * plyDiff );
	}

	[GameEvent.Player.Spawned]
	private static void Apply( Player player )
	{
		if ( GameManager.Current.State is not PreRound )
			return;

		player.TimeUntilClean = 0;

		if ( !Enabled || player.BaseKarma >= StartValue )
		{
			player.DamageFactor = 1f;
			return;
		}

		var k = player.BaseKarma - StartValue;
		var damageFactor = 1 + (0.0007f * k) + (-0.000002f * (k * k));

		player.DamageFactor = Math.Clamp( damageFactor, 0.1f, 1f );
	}


	[GameEvent.Player.TookDamage]
	private static void OnPlayerTookDamage( Player player )
	{
		if ( !Game.IsServer )
			return;

		if ( GameManager.Current.State is not InProgress )
			return;

		var attacker = player.LastAttacker as Player;

		if ( !attacker.IsValid() || !player.IsValid() )
			return;

		if ( attacker == player )
			return;

		var damage = player.LastDamage.Damage;

		if ( attacker.Team == player.Team )
		{
			if ( !player.TimeUntilClean )
				return;

			if ( player.LastDamage.IsAvoidable() )
				return;

			var penalty = GetHurtPenalty( player.ActiveKarma, damage, attacker.Role.Karma.TeamHurtPenaltyMultiplier );
			GivePenalty( attacker, penalty );
		}
		else
		{
			var reward = GetHurtReward( damage, player.Role.Karma.AttackerHurtRewardMultiplier );
			GiveReward( attacker, reward );
		}
	}

	[GameEvent.Player.Killed]
	private static void OnPlayerKilled( Player player )
	{
		if ( !Game.IsServer )
			return;

		if ( GameManager.Current.State is not InProgress )
			return;

		var attacker = player.LastAttacker as Player;

		if ( !attacker.IsValid() || !player.IsValid() )
			return;

		if ( attacker == player )
			return;

		if ( attacker.Team == player.Team )
		{
			if ( !player.TimeUntilClean )
				return;

			if ( player.LastDamage.IsAvoidable() )
				return;

			var penalty = GetKillPenalty( player.ActiveKarma, attacker.Role.Karma.TeamKillPenaltyMultiplier );
			GivePenalty( attacker, penalty );
		}
		else
		{
			var reward = GetKillReward( player.Role.Karma.AttackerKillRewardMultiplier );
			GiveReward( attacker, reward );
		}
	}

	private static void RoundIncrement( Player player )
	{
		if ( (!player.IsAlive && !player.KilledByPlayer) || player.IsSpectator )
			return;

		var reward = RoundHeal;

		if ( player.TimeUntilClean )
			reward += CleanBonus;

		GiveReward( player, reward );
	}

	private static bool CheckAutoKick( Player player )
	{
		return LowAutoKick && player.BaseKarma < MinValue;
	}

	[GameEvent.Round.End]
	private static void OnRoundEnd( Team winningTeam, WinType winType )
	{
		if ( !Game.IsServer )
			return;

		foreach ( var client in Game.Clients )
		{
			var player = client.Pawn as Player;

			RoundIncrement( player );
			Rebase( player );

			if ( Enabled && CheckAutoKick( player ) )
				client.Kick();
		}
	}

	private static void Rebase( Player player )
	{
		player.BaseKarma = player.ActiveKarma;
	}

	[GameEvent.Client.Disconnected]
	private static void SaveKarma( IClient client )
	{
		SavedPlayerValues[client.SteamId] = (client.Pawn as Player).ActiveKarma;
	}
}
