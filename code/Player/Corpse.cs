using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.Physics;
using Sandbox.UI;
using System.Collections.Generic;
using System.Linq;

namespace TTT;

[ClassName( "ttt_entity_corpse" )]
[HideInEditor]
[Title( "Player corpse" )]
public partial class Corpse : ModelEntity, IEntityHint, IUse
{
	[Net] public bool HasCredits { get; private set; }
	public Player Player { get; set; }
	/// <summary>
	/// Whether or not this corpse has been found by a player
	/// or revealed at the end of a round.
	/// </summary>
	public bool IsFound { get; set; }
	/// <summary>
	/// The player who identified this corpse (this does not include covert searches).
	/// </summary>
	public Player Finder { get; private set; }
	public TimeUntil TimeUntilDNADecay { get; private set; }
	public string C4Note { get; private set; }
	public string LastWords { get; private set; }
	public PerkInfo[] Perks { get; private set; }
	public Player[] KillList { get; private set; }
	public bool HasCalledDetective { get; set; } = false;
	public List<Particles> Ropes { get; private set; } = new();
	public List<PhysicsJoint> RopeJoints { get; private set; } = new();

	// We use this HashSet of NetworkIds to avoid sending kill information
	// to players multiple times.
	private readonly HashSet<int> _playersWithKillInfo = new();

	public Corpse() { }

	public Corpse( Player player )
	{
		Game.AssertServer();

		#region Copy Player
		Player = player;
		HasCredits = player.Credits > 0;
		Owner = player;
		Transform = player.Transform;
		Model = player.Model;

		this.CopyBonesFrom( player );
		this.SetRagdollVelocityFrom( player );
		ApplyForceToBone( Player.LastDamage.Force, Player.LastDamage.BoneIndex );
		CopyBodyGroups( player );
		#endregion

		#region Copy Clothes
		foreach ( var child in Player.Children )
		{
			if ( !child.Tags.Has( "clothes" ) )
				continue;

			if ( child is not ModelEntity modelEntity )
				continue;

			var clothing = new ModelEntity
			{
				Model = modelEntity.Model,
				Position = modelEntity.Position,
				RenderColor = modelEntity.RenderColor
			};

			clothing.TakeDecalsFrom( modelEntity );
			clothing.CopyBodyGroups( modelEntity );
			clothing.CopyMaterialGroup( modelEntity );

			if ( Player.LastDamage.IsHeadshot() && clothing.Model?.ResourcePath == Detective.Hat.Model )
			{
				clothing.Tags.Add( "interactable" );
				clothing.SetupPhysicsFromModel( PhysicsMotionType.Dynamic );
				clothing.PhysicsGroup.AddVelocity( Player.LastDamage.Force * 2 );
			}
			else
				clothing.SetParent( this, true );
		}
		#endregion

		if ( Player.LastDamage.HasTag( DamageTags.Bullet ) && Player.LastAttacker is Player killer )
		{
			var dna = new DNA( killer );
			Components.Add( dna );
			TimeUntilDNADecay = dna.TimeUntilDecayed;
		}

		var c4Note = player.Components.Get<C4Note>();
		if ( c4Note is not null )
			C4Note = c4Note.SafeWireNumber.ToString();

		Perks = new PerkInfo[Player.Perks.Count];
		for ( var i = 0; i < Player.Perks.Count; i++ )
			Perks[i] = Player.Perks[i].Info;

		LastWords = player.LastWords;
		KillList = Player.PlayersKilled.ToArray();

		_playersWithKillInfo.Add( player.NetworkIdent );
	}

	public override void Spawn()
	{
		base.Spawn();

		Tags.Add( "interactable", "corpse" );

		PhysicsEnabled = true;
		UsePhysicsCollision = true;
	}

	public override void ClientSpawn()
	{
		base.ClientSpawn();

		TakeDecalsFrom( Owner as ModelEntity );
	}

	/// <summary>
	/// Search this corpse.
	/// </summary>
	/// <param name="searcher">The player who is searching this corpse.</param>
	/// <param name="covert">Whether or not this is a covert search.</param>
	/// <param name="retrieveCredits">Should the searcher retrieve credits.</param>
	public void Search( Player searcher, bool covert = false, bool retrieveCredits = true )
	{
		Game.AssertServer();
		Assert.NotNull( searcher );

		var creditsRetrieved = 0;
		retrieveCredits &= searcher.Role.CanRetrieveCredits & searcher.IsAlive;

		if ( retrieveCredits && HasCredits )
		{
			searcher.Credits += Player.Credits;
			creditsRetrieved = Player.Credits;
			Player.Credits = 0;
			HasCredits = false;
		}

		SendPlayer( To.Single( searcher ) );
		Player.SendRole( To.Single( searcher ) );
		SendKillInfo( To.Single( searcher ) );

		// Dead players will always covert search.
		covert |= !searcher.IsAlive;

		if ( !covert )
		{
			if ( !IsFound )
			{
				SendPlayer( To.Everyone );
				Player.IsRoleKnown = true;
			}

			// If the searcher is a detective, send kill info to everyone.
			if ( searcher.Role is Detective )
				SendKillInfo( To.Everyone );

			if ( !Player.IsConfirmedDead )
				Player.ConfirmDeath( searcher );

			foreach ( var deadPlayer in Player.PlayersKilled )
			{
				if ( deadPlayer.IsConfirmedDead )
					continue;

				deadPlayer.ConfirmDeath( searcher );

				UI.InfoFeed.AddPlayerToPlayerEntry
				(
					searcher,
					deadPlayer,
					"confirmed the death of"
				);
			}

			if ( !IsFound )
			{
				IsFound = true;
				Finder = searcher;
				Event.Run( GameEvent.Player.CorpseFound, Player );
				ClientCorpseFound( searcher );
			}
		}

		ClientSearch( To.Single( searcher ), creditsRetrieved );
	}

	public void SendPlayer( To to )
	{
		SendPlayer( to, Player );
	}

	public void SendKillInfo( To to )
	{
		Game.AssertServer();

		foreach ( var client in to )
		{
			if ( _playersWithKillInfo.Contains( client.Pawn.NetworkIdent ) )
				continue;

			_playersWithKillInfo.Add( client.Pawn.NetworkIdent );

			Player.SendDamageInfo( To.Single( client ) );

			SendMiscInfo( To.Single( client ), KillList, Perks, C4Note, LastWords, TimeUntilDNADecay );

			if ( client.Pawn is Player player && player.Role is Detective )
				SendDetectiveInfo( To.Single( client ), Player.LastSeenPlayer );
		}
	}

	[ClientRpc]
	public void ClientCorpseFound( Player finder, bool wasPreviouslyFound = false )
	{
		IsFound = true;
		Finder = finder;

		if ( Finder.IsValid() && !wasPreviouslyFound )
			Event.Run( GameEvent.Player.CorpseFound, Player );
	}

	[ClientRpc]
	private void ClientSearch( int creditsRetrieved = 0 )
	{
		Player.Status = Player.IsConfirmedDead ? PlayerStatus.ConfirmedDead : PlayerStatus.MissingInAction;

		foreach ( var deadPlayer in Player.PlayersKilled )
			deadPlayer.Status = deadPlayer.IsConfirmedDead ? PlayerStatus.ConfirmedDead : PlayerStatus.MissingInAction;

		if ( creditsRetrieved <= 0 )
			return;

		UI.InfoFeed.AddEntry
		(
			Game.LocalPawn as Player,
			$"found {creditsRetrieved} credits!"
		);
	}

	[ClientRpc]
	private void SendMiscInfo( Player[] killList, PerkInfo[] perks, string c4Note, string lastWords, TimeUntil dnaDecay )
	{
		if ( killList is not null )
			Player.PlayersKilled = killList.ToList();

		Perks = perks;
		C4Note = c4Note;
		LastWords = lastWords;
		TimeUntilDNADecay = dnaDecay;
	}

	[ClientRpc]
	private void SendPlayer( Player player )
	{
		Player = player;
		Player.Corpse = this;
	}

	/// <summary>
	/// Detectives get additional information about a corpse.
	/// </summary>
	[ClientRpc]
	private void SendDetectiveInfo( Player player )
	{
		Player.LastSeenPlayer = player;
	}

	public void RemoveRopeAttachments()
	{
		foreach ( var rope in Ropes )
			rope.Destroy( true );

		foreach ( var spring in RopeJoints )
			spring.Remove();

		Ropes.Clear();
		RopeJoints.Clear();
	}

	protected override void OnDestroy()
	{
		RemoveRopeAttachments();

		base.OnDestroy();
	}

	private void ApplyForceToBone( Vector3 force, int forceBone )
	{
		PhysicsGroup.AddVelocity( force );

		if ( forceBone < 0 )
			return;

		var corpse = GetBonePhysicsBody( forceBone );

		if ( corpse is not null )
			corpse.ApplyForce( force * 1000 );
		else
			PhysicsGroup.AddVelocity( force );
	}

	float IEntityHint.HintDistance => Player.MaxHintDistance;

	bool IEntityHint.CanHint( Player player ) => GameManager.Current.State is InProgress or PostRound;

	Panel IEntityHint.DisplayHint( Player player ) => new UI.CorpseHint( this );

	void IEntityHint.Tick( Player player )
	{
		if ( Input.Down( GetSearchButton() ) && CanSearch() )
		{
			var hasCorpseInfo = Player.IsValid() && !Player.LastDamage.Equals( default( DamageInfo ) );

			// Dead player wants to view the body, request a convert search.
			if ( !player.IsAlive && !hasCorpseInfo )
				ConvertSearch( NetworkIdent );

			if ( Player.IsValid() && hasCorpseInfo )
				UI.FullScreenHintMenu.Instance?.Open( new UI.InspectMenu( this ) );

			return;
		}

		UI.FullScreenHintMenu.Instance?.Close();
	}

	bool IUse.OnUse( Entity user )
	{
		Search( user as Player, Input.Down( InputButton.Run ) );
		return false;
	}

	bool IUse.IsUsable( Entity user )
	{
		return GameManager.Current.State is InProgress || GameManager.Current.State is PostRound;
	}

	public bool CanSearch()
	{
		Game.AssertClient();

		if ( Game.LocalPawn is not Player player )
			return false;

		if ( GetSearchButton() == InputButton.PrimaryAttack )
			return true;

		return player.CanUse( this );
	}

	public static InputButton GetSearchButton()
	{
		Game.AssertClient();

		var player = Game.LocalPawn as Player;

		if ( player.ActiveCarriable is not Binoculars binoculars )
			return InputButton.Use;

		if ( !binoculars.IsZoomed )
			return InputButton.Use;

		return InputButton.PrimaryAttack;
	}

	[ConCmd.Server]
	private static void ConvertSearch( int networkIdent )
	{
		if ( ConsoleSystem.Caller.Pawn is not Player player || FindByIndex( networkIdent ) is not Corpse corpse )
			return;

		corpse.Search( player, true, false );
	}
}
